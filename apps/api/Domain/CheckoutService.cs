
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using Restaurante.Api.Integration;
using Restaurante.Api.Persistence;
using static Restaurante.Api.Persistence.Database;

namespace Restaurante.Api.Domain;

public sealed class CheckoutService(Database database, IHrAdapter hr, TimeProvider clock)
{
    internal const string RuleSql = """
        SELECT name, description, starts_at, ends_at, price_cents, subsidy_cents, meal_limit,
            daily_limit_cents, monthly_limit_cents, version FROM meal_rules WHERE company_id = @CompanyId
        """;
    internal const string ProductCols = """
        id, name, description, category, price_cents, subsidy_cents, barcode, image_key, available, version
        """;
    private const string EmployeeCols = """
        e.id, e.rf, e.name, e.department, e.employment, e.external_id, e.active, e.eligible, e.payroll_allowed
        """;

    public async Task<Catalog> Catalog(Actor actor, CancellationToken ct)
    {
        await using var conn = await database.Open(ct);
        var (date, time) = Pricing.BusinessClock(clock.GetUtcNow(), actor.Timezone);
        var rule = await Rule(conn, null, actor.CompanyId, ct);
        var products = await conn.QueryAsync<Product>(Command(
            $"SELECT {ProductCols} FROM products WHERE company_id = @CompanyId AND available ORDER BY category, name",
            new { actor.CompanyId }, null, ct));
        return new(products.AsList(), rule, date, actor.Timezone, time >= rule.StartsAt && time < rule.EndsAt);
    }

    public async Task<Identification> Identify(Actor actor, string kind, string input, CancellationToken ct)
    {
        var value = input.Trim();
        if (kind == "rf" && (value.Length > 32 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            throw new BusinessError(400, "invalid_rf", "Informe um RF válido.");
        var now = clock.GetUtcNow();
        var (date, time) = Pricing.BusinessClock(now, actor.Timezone);
        Employee? employee;
        await using (var conn = await database.Open(ct))
        {
            employee = kind == "rf"
                ? await conn.QuerySingleOrDefaultAsync<Employee>(Command(
                    $"SELECT {EmployeeCols} FROM employees e WHERE e.company_id = @CompanyId AND e.rf = @rf",
                    new { actor.CompanyId, rf = value.ToUpperInvariant() }, null, ct))
                : await conn.QuerySingleOrDefaultAsync<Employee>(Command($"""
                    SELECT {EmployeeCols} FROM badges b JOIN employees e
                        ON e.company_id = b.company_id AND e.id = b.employee_id
                    WHERE b.company_id = @CompanyId AND b.active AND b.token_hash = @hash
                    """, new { actor.CompanyId, hash = SHA256.HashData(Encoding.UTF8.GetBytes(value)) }, null, ct));
        }
        if (employee is null)
        {
            await Record(actor, "identification_not_found", null, ct);
            throw new BusinessError(404, "employee_not_found", "Funcionário não encontrado. Confira o RF ou leia o crachá novamente.");
        }

        var eligibility = await hr.Check(employee, date, ct);
        await using var connection = await database.Open(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var current = await EmployeeById(connection, tx, actor.CompanyId, employee.Id, true, ct);
        if (!current.Active || !current.Eligible || !eligibility.Allowed)
        {
            await Audit(connection, tx, actor, "identification_blocked", employee.Id, null, ct);
            await tx.CommitAsync(ct);
            throw new BusinessError(403, "employee_ineligible", "O funcionário não está elegível para atendimento.");
        }

        var existing = await connection.QuerySingleOrDefaultAsync<CheckoutSession>(Command("""
            SELECT employee_id, operator_id, terminal_id, token, expires_at FROM checkout_sessions WHERE employee_id = @Id
            """, new { employee.Id }, tx, ct));
        if (existing is not null && existing.ExpiresAt > now.UtcDateTime &&
            (existing.OperatorId != actor.Id || existing.TerminalId != actor.TerminalId))
        {
            await Audit(connection, tx, actor, "concurrent_identification", employee.Id, null, ct);
            await tx.CommitAsync(ct);
            throw new BusinessError(409, "employee_in_use", "Já existe um atendimento aberto para esse funcionário em outro caixa ou sessão.");
        }

        var checkout = existing is not null && existing.ExpiresAt > now.UtcDateTime ? existing.Token : Guid.NewGuid();
        var expiresAt = now.UtcDateTime.AddMinutes(5);
        await connection.ExecuteAsync(Command("""
            INSERT INTO checkout_sessions (employee_id,company_id,terminal_id,operator_id,token,expires_at)
            VALUES (@Id,@CompanyId,@TerminalId,@operatorId,@checkout,@expiresAt)
            ON CONFLICT (employee_id) DO UPDATE SET terminal_id=EXCLUDED.terminal_id, operator_id=EXCLUDED.operator_id,
                token=EXCLUDED.token, expires_at=EXCLUDED.expires_at
            """, new { employee.Id, actor.CompanyId, actor.TerminalId, operatorId = actor.Id, checkout, expiresAt }, tx, ct));
        var usage = await ReadUsage(connection, tx, actor, employee.Id, date, ct);
        var rule = await Rule(connection, tx, actor.CompanyId, ct);
        var payrollAllowed = eligibility.PayrollAllowed && current.PayrollAllowed;
        var block = Pricing.MealBlock(rule, usage, time) ??
            (rule.PriceCents > rule.SubsidyCents && !payrollAllowed ? Pricing.PayrollRequired : null);
        await Audit(connection, tx, actor, "employee_identified", employee.Id, JsonSerializer.Serialize(new { kind }), ct);
        await tx.CommitAsync(ct);
        return new(ToView(current), checkout, expiresAt, payrollAllowed, block is null, block);
    }

    public async Task<SpendingHistory> Spending(Actor actor, Guid token, int page, CancellationToken ct)
    {
        const int pageSize = 20;
        if (page is < 1 or > 1000)
            throw new BusinessError(400, "invalid_page", "Informe uma página válida para consultar os gastos.");

        var instant = clock.GetUtcNow();
        var (today, _) = Pricing.BusinessClock(instant, actor.Timezone);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        await using var connection = await database.Open(ct);
        await using var tx = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var session = await Session(connection, tx, actor, token, instant.UtcDateTime, false, ct);
        var employee = await EmployeeById(connection, tx, actor.CompanyId, session.EmployeeId, false, ct);
        if (!employee.Active || !employee.Eligible)
            throw new BusinessError(403, "employee_ineligible", "O funcionário não está elegível para atendimento.");

        const string scope = """
            WHERE o.company_id=@CompanyId AND o.unit_id=@UnitId AND o.employee_id=@EmployeeId AND
                o.status='confirmed' AND o.business_date BETWEEN @monthStart AND @today
            """;
        var args = new { actor.CompanyId, actor.UnitId, session.EmployeeId, monthStart, today,
            limit = pageSize + 1, offset = pageSize * (page - 1) };
        var totals = await connection.QuerySingleAsync<SpendingRows>(Command($"""
            SELECT COALESCE(SUM(p.amount_cents) FILTER (WHERE o.business_date=@today),0)::bigint AS today_total,
                COALESCE(SUM(p.amount_cents) FILTER (WHERE o.business_date=@today AND p.method='payroll'),0)::bigint AS today_payroll,
                COALESCE(SUM(p.amount_cents),0)::bigint AS month_total,
                COALESCE(SUM(p.amount_cents) FILTER (WHERE p.method='payroll'),0)::bigint AS month_payroll
            FROM orders o JOIN payments p ON p.order_id=o.id {scope}
            """, args, tx, ct));
        var extras = (await connection.QueryAsync<ExtraPurchase>(Command($"""
            SELECT o.id AS order_id, i.position, i.name, i.quantity,
                (i.quantity::bigint * (i.unit_price_cents - i.unit_subsidy_cents)) AS net_cents,
                o.created_at, t.name AS terminal
            FROM orders o JOIN order_items i ON i.order_id=o.id JOIN terminals t ON t.id=o.terminal_id
            {scope} AND i.product_id IS NOT NULL
            ORDER BY o.created_at DESC, o.id DESC, i.position LIMIT @limit OFFSET @offset
            """, args, tx, ct))).AsList();
        await Audit(connection, tx, actor, "employee_spending_viewed", session.EmployeeId, null, ct);
        await tx.CommitAsync(ct);
        return new(actor.UnitName, new(totals.TodayTotal, totals.TodayPayroll, totals.TodayTotal - totals.TodayPayroll),
            new(totals.MonthTotal, totals.MonthPayroll, totals.MonthTotal - totals.MonthPayroll),
            extras.Take(pageSize).ToArray(), extras.Count > pageSize);
    }

    public async Task Release(Actor actor, Guid token, CancellationToken ct)
    {
        await using var connection = await database.Open(ct);
        await connection.ExecuteAsync(Command("""
            DELETE FROM checkout_sessions WHERE token = @token AND company_id = @CompanyId
                AND operator_id = @operatorId AND terminal_id = @TerminalId
            """, new { token, actor.CompanyId, operatorId = actor.Id, actor.TerminalId }, null, ct));
    }

    public async Task<Quote> Quote(Actor actor, Cart cart, CancellationToken ct)
    {
        Pricing.CheckCart(cart);
        var (employee, eligibility, instant) = await CheckEmployee(actor, cart.CheckoutToken, ct);
        await using var connection = await database.Open(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        return await QuoteInTransaction(connection, tx, actor, cart, employee.Id, eligibility, instant, ct);
    }

    public async Task<Receipt> Confirm(Actor actor, Finalization request, Guid key, CancellationToken ct)
    {
        var cart = new Cart(request.CheckoutToken, request.IncludesMeal, request.Items);
        Pricing.CheckCart(cart);
        if (request.Reference?.Length > 100 || request.QuoteVersion?.Length != 64 ||
            request.Method is not ("subsidy" or "payroll" or "cash" or "card" or "pix"))
            throw new BusinessError(400, "invalid_confirmation", "Confirme com uma cotação vigente e um meio de pagamento válido.");

        var reference = string.IsNullOrWhiteSpace(request.Reference) || request.Method is "payroll" or "subsidy"
            ? null : request.Reference.Trim();
        var hash = Pricing.Hash(new
        {
            request.CheckoutToken, request.IncludesMeal, items = request.Items.OrderBy(i => i.ProductId),
            request.QuoteVersion, request.Method, request.Received, reference
        });
        await using var connection = await database.Open(ct);
        Employee? employee = null;
        try
        {
            var already = await Replay(connection, null, actor, key, hash, ct);
            if (already is Guid id) return await ReceiptById(connection, null, actor, id, ct);
            Eligibility eligibility;
            DateTimeOffset instant;
            try { (employee, eligibility, instant) = await CheckEmployee(actor, cart.CheckoutToken, ct); }
            catch (BusinessError ex) when (ex.Code == "checkout_expired")
            {
                var recovered = await Replay(connection, null, actor, key, hash, ct);
                if (recovered is Guid receiptId) return await ReceiptById(connection, null, actor, receiptId, ct);
                throw;
            }
            await using var tx = await connection.BeginTransactionAsync(ct);
            await connection.ExecuteAsync(Command("SELECT id FROM staff_users WHERE id = @Id FOR UPDATE",
                new { actor.Id }, tx, ct));
            already = await Replay(connection, tx, actor, key, hash, ct);
            if (already is Guid existingId) return await ReceiptById(connection, tx, actor, existingId, ct);
            var quote = await QuoteInTransaction(connection, tx, actor, cart, employee.Id, eligibility, instant, ct);
            if (!string.Equals(quote.Version, request.QuoteVersion, StringComparison.Ordinal))
                throw new BusinessError(409, "quote_changed", "Os valores ou os itens mudaram. Confira o pedido novamente.");
            var payments = Pricing.Payments(quote, request.Method, request.Received, reference);
            var orderId = Guid.NewGuid();
            await connection.ExecuteAsync(Command("""
                INSERT INTO orders (id,company_id,unit_id,terminal_id,employee_id,operator_id,business_date,
                    created_at,status,includes_meal,subtotal_cents,subsidy_cents,total_cents,idempotency_key,request_hash,checkout_token)
                VALUES (@orderId,@CompanyId,@UnitId,@TerminalId,@employeeId,@operatorId,@BusinessDate,
                    @createdAt,'confirmed',@IncludesMeal,@SubtotalCents,@SubsidyCents,@TotalCents,@key,@hash,@CheckoutToken);
                DELETE FROM checkout_sessions WHERE token = @CheckoutToken;
                """, new { orderId, actor.CompanyId, actor.UnitId, actor.TerminalId, employeeId = employee.Id,
                    operatorId = actor.Id, quote.BusinessDate, createdAt = clock.GetUtcNow().UtcDateTime, cart.IncludesMeal,
                    quote.SubtotalCents, quote.SubsidyCents, quote.TotalCents, key, hash, cart.CheckoutToken }, tx, ct));
            foreach (var payment in payments)
            {
                await connection.ExecuteAsync(Command("""
                    INSERT INTO payments (order_id,method,amount_cents,reference) VALUES (@orderId,@Method,@AmountCents,@Reference)
                    """, new { orderId, payment.Method, payment.AmountCents, payment.Reference }, tx, ct));
            }
            for (var i = 0; i < quote.Items.Count; i++)
            {
                var item = quote.Items[i];
                await connection.ExecuteAsync(Command("""
                    INSERT INTO order_items (order_id,position,product_id,name,quantity,unit_price_cents,unit_subsidy_cents)
                    VALUES (@orderId,@i,@ProductId,@Name,@Quantity,@UnitPriceCents,@UnitSubsidyCents)
                    """, new { orderId, i, item.ProductId, item.Name, item.Quantity,
                        item.UnitPriceCents, item.UnitSubsidyCents }, tx, ct));
            }
            await OutboxWorker.Enqueue(connection, tx,
                new(orderId, employee.ExternalId, quote.BusinessDate, "LUNCH", quote.TotalCents, quote.SubsidyCents, quote.Items, false),
                cart.IncludesMeal, quote.MealPayrollCents, ct);
            await Audit(connection, tx, actor, "order_confirmed", orderId,
                JsonSerializer.Serialize(new { quote.TotalCents, quote.SubsidyCents,
                    payments = payments.Select(p => new { p.Method, p.AmountCents }) }), ct);
            var receipt = await ReceiptById(connection, tx, actor, orderId, ct);
            await tx.CommitAsync(ct);
            return receipt;
        }
        catch (BusinessError ex) when (ex.Code is "duplicate_meal" or "spending_limit" or "idempotency_conflict")
        {
            await Record(actor, ex.Code, employee?.Id, ct);
            throw;
        }
    }

    public async Task<Receipt> GetReceipt(Actor actor, Guid id, CancellationToken ct)
    {
        await using var connection = await database.Open(ct);
        return await ReceiptById(connection, null, actor, id, ct);
    }

    public async Task<Receipt> Reverse(Actor actor, Guid id, string reason, string? refundReference, CancellationToken ct)
    {
        reason = reason.Trim();
        if (reason.Length < 8) throw new BusinessError(400, "reason_required", "Informe o motivo do estorno.");
        await using var connection = await database.Open(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var first = await ReceiptById(connection, tx, actor, id, ct);
        var employee = await EmployeeById(connection, tx, actor.CompanyId, first.Employee.Id, true, ct);
        var receipt = await ReceiptById(connection, tx, actor, id, ct);
        if (receipt.Status == "reversed") throw new BusinessError(409, "already_reversed", "O atendimento já foi estornado.");
        var reference = string.IsNullOrWhiteSpace(refundReference) ? null : refundReference.Trim();
        if (receipt.Payments.Any(p => p.Method is "cash" or "card" or "pix") && reference is null)
            throw new BusinessError(422, "refund_required", "Registre a referência da devolução antes de estornar.");
        await connection.ExecuteAsync(Command("""
            UPDATE orders SET status = 'reversed' WHERE id = @id;
            INSERT INTO reversals (order_id,actor_id,reason,refund_reference,reversed_at)
            VALUES (@id,@actorId,@reason,@reference,now());
            """, new { id, actorId = actor.Id, reason, reference }, tx, ct));
        await OutboxWorker.Enqueue(connection, tx,
            new(id, employee.ExternalId, receipt.BusinessDate, "LUNCH", -receipt.TotalCents, -receipt.SubsidyCents,
                receipt.Items, true),
            receipt.Items.Any(i => i.ProductId is null),
            -receipt.Payments.Where(p => p.Method == "payroll").Sum(p => p.AmountCents), ct);
        await Audit(connection, tx, actor, "order_reversed", id,
            JsonSerializer.Serialize(new { receipt.TotalCents,
                payments = receipt.Payments.Select(p => new { p.Method, p.AmountCents }) }), ct);
        await tx.CommitAsync(ct);
        return receipt with { Status = "reversed" };
    }

    private async Task<(Employee, Eligibility, DateTimeOffset)> CheckEmployee(Actor actor, Guid token, CancellationToken ct)
    {
        var instant = clock.GetUtcNow();
        Employee employee;
        await using (var connection = await database.Open(ct))
        {
            var session = await Session(connection, null, actor, token, instant.UtcDateTime, false, ct);
            employee = await EmployeeById(connection, null, actor.CompanyId, session.EmployeeId, false, ct);
        }
        var (date, _) = Pricing.BusinessClock(instant, actor.Timezone);
        var eligibility = await hr.Check(employee, date, ct);
        if (Pricing.BusinessClock(clock.GetUtcNow(), actor.Timezone).Date != date)
            throw new BusinessError(409, "business_date_changed", "A data comercial mudou. Identifique o funcionário novamente.");
        return (employee, eligibility, clock.GetUtcNow());
    }

    private async Task<Quote> QuoteInTransaction(NpgsqlConnection connection, NpgsqlTransaction tx, Actor actor, Cart cart,
        Guid employeeId, Eligibility eligibility, DateTimeOffset instant, CancellationToken ct)
    {
        var current = await EmployeeById(connection, tx, actor.CompanyId, employeeId, true, ct);
        await Session(connection, tx, actor, cart.CheckoutToken, clock.GetUtcNow().UtcDateTime, true, ct);
        var rule = await Rule(connection, tx, actor.CompanyId, ct);
        var products = await connection.QueryAsync<Product>(Command($"""
            SELECT {ProductCols} FROM products WHERE company_id = @CompanyId AND id = ANY(@ids) ORDER BY id FOR SHARE
            """, new { actor.CompanyId, ids = cart.Items.Select(x => x.ProductId).ToArray() }, tx, ct));
        var (date, time) = Pricing.BusinessClock(clock.GetUtcNow(), actor.Timezone);
        if (date != Pricing.BusinessClock(instant, actor.Timezone).Date)
            throw new BusinessError(409, "business_date_changed", "A data comercial mudou. Identifique o funcionário novamente.");
        var usage = await ReadUsage(connection, tx, actor, employeeId, date, ct);
        return Pricing.Calculate(cart, rule, products.AsList(), usage,
            new(eligibility.Allowed && current.Active && current.Eligible, eligibility.PayrollAllowed && current.PayrollAllowed), date, time);
    }

    private static async Task<Guid?> Replay(NpgsqlConnection connection, NpgsqlTransaction? tx, Actor actor, Guid key,
        string hash, CancellationToken ct)
    {
        var record = await connection.QuerySingleOrDefaultAsync<Existing>(Command("""
            SELECT id, request_hash, company_id, unit_id, terminal_id
            FROM orders WHERE operator_id = @Id AND idempotency_key = @key
            """, new { actor.Id, key }, tx, ct));
        if (record is not null && (record.RequestHash != hash || record.CompanyId != actor.CompanyId ||
            record.UnitId != actor.UnitId || record.TerminalId != actor.TerminalId))
            throw new BusinessError(409, "idempotency_conflict", "A chave de confirmação já foi usada para outro pedido.");
        return record?.Id;
    }

    private static async Task<CheckoutSession> Session(NpgsqlConnection connection, NpgsqlTransaction? tx, Actor actor,
        Guid token, DateTime instant, bool lockRow, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<CheckoutSession>(Command("""
            SELECT employee_id, operator_id, terminal_id, token, expires_at FROM checkout_sessions
            WHERE token = @token AND company_id = @CompanyId AND operator_id = @Id AND terminal_id = @TerminalId
                AND expires_at > @instant
            """ + (lockRow ? " FOR UPDATE" : ""), new { token, actor.CompanyId, actor.Id, actor.TerminalId, instant }, tx, ct))
        ?? throw new BusinessError(409, "checkout_expired", "O atendimento expirou. Identifique o funcionário novamente.");

    private static async Task<Employee> EmployeeById(NpgsqlConnection connection, NpgsqlTransaction? tx, Guid companyId,
        Guid employeeId, bool lockRow, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<Employee>(Command(
            $"SELECT {EmployeeCols} FROM employees e WHERE e.company_id = @companyId AND e.id = @employeeId" +
            (lockRow ? " FOR UPDATE" : ""), new { companyId, employeeId }, tx, ct))
        ?? throw new BusinessError(404, "employee_not_found", "Funcionário não encontrado.");

    private static async Task<MealRule> Rule(NpgsqlConnection connection, NpgsqlTransaction? tx, Guid companyId, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<MealRule>(Command(RuleSql + (tx is null ? "" : " FOR SHARE"),
            new { companyId }, tx, ct)) ?? throw new BusinessError(409, "missing_rule", "Configure a regra de almoço.");

    private static Task<Usage> ReadUsage(NpgsqlConnection connection, NpgsqlTransaction tx, Actor actor,
        Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var monthStart = new DateOnly(date.Year, date.Month, 1);
        return connection.QuerySingleAsync<Usage>(Command("""
            SELECT COALESCE(SUM(total_cents) FILTER (WHERE business_date=@date),0)::bigint AS daily_cents,
                COALESCE(SUM(total_cents),0)::bigint AS monthly_cents,
                (COUNT(*) FILTER (WHERE business_date=@date AND includes_meal))::int AS meals
            FROM orders WHERE company_id=@CompanyId AND employee_id=@employeeId AND status='confirmed'
                AND business_date >= @monthStart AND business_date < @nextMonth
            """, new { actor.CompanyId, employeeId, date, monthStart, nextMonth = monthStart.AddMonths(1) }, tx, ct));
    }

    private async Task Record(Actor actor, string action, Guid? target, CancellationToken ct)
    {
        await using var conn = await database.Open(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await Audit(conn, tx, actor, action, target, null, ct);
        await tx.CommitAsync(ct);
    }

    private static EmployeeView ToView(Employee e) => new(e.Id, e.Rf, e.Name, e.Department, e.Employment);

    private static async Task<Receipt> ReceiptById(NpgsqlConnection connection, NpgsqlTransaction? tx, Actor actor,
        Guid id, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ReceiptRow>(Command("""
            SELECT o.id, o.employee_id, e.rf, e.name, e.department, e.employment, o.created_at, o.business_date,
                t.name AS terminal, s.display_name AS operator, o.status,
                o.subtotal_cents,o.subsidy_cents,o.total_cents
            FROM orders o JOIN employees e ON e.id=o.employee_id
                JOIN staff_users s ON s.id=o.operator_id JOIN terminals t ON t.id=o.terminal_id
            WHERE o.id=@id AND o.company_id=@CompanyId AND o.unit_id=@UnitId AND
                (@Role <> 'operator' OR o.operator_id=@actorId)
            """, new { id, actor.CompanyId, actor.UnitId, actor.Role, actorId = actor.Id }, tx, ct))
            ?? throw new BusinessError(404, "order_not_found", "Atendimento não encontrado.");
        var lines = await connection.QueryAsync<PricedLine>(Command("""
            SELECT product_id,name,quantity,unit_price_cents,unit_subsidy_cents
            FROM order_items WHERE order_id=@id ORDER BY position
            """, new { id }, tx, ct));
        var payments = (await connection.QueryAsync<Payment>(Command("""
            SELECT method, amount_cents, reference FROM payments WHERE order_id=@id ORDER BY (method = 'payroll'), method
            """, new { id }, tx, ct))).AsList();
        var primary = payments[0];
        return new(row.Id, new(row.EmployeeId, row.Rf, row.Name, row.Department, row.Employment), row.CreatedAt,
            row.BusinessDate, row.Terminal, row.Operator, row.Status, lines.AsList(), row.SubtotalCents, row.SubsidyCents,
            row.TotalCents, primary.Method, primary.Reference, payments);
    }

    private sealed record SpendingRows(long TodayTotal, long TodayPayroll, long MonthTotal, long MonthPayroll);
    private sealed record Existing(Guid Id, string RequestHash, Guid CompanyId, Guid UnitId, Guid TerminalId);
    private sealed record ReceiptRow(Guid Id, Guid EmployeeId, string Rf, string Name, string Department, string Employment,
        DateTime CreatedAt, DateOnly BusinessDate, string Terminal, string Operator, string Status,
        long SubtotalCents, long SubsidyCents, long TotalCents);
}
