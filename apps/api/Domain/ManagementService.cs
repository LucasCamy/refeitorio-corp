
using Dapper;
using Npgsql;
using Restaurante.Api.Contracts;
using Restaurante.Api.Persistence;
using static Restaurante.Api.Persistence.Database;

namespace Restaurante.Api.Domain;

public sealed class ManagementService(Database database, TimeProvider clock, IConfiguration config)
{
    public async Task<IReadOnlyList<Product>> Products(Actor actor, CancellationToken ct)
    {
        await using var connection = await database.Open(ct);
        return (await connection.QueryAsync<Product>(Command($"""
            SELECT {CheckoutService.ProductCols} FROM products WHERE company_id=@CompanyId ORDER BY name
            """, new { actor.CompanyId }, null, ct))).AsList();
    }

    public async Task<Product> SaveProduct(Actor actor, Guid? existing, ProductRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (name.Length == 0 || request.SubsidyCents > request.PriceCents)
            throw new BusinessError(422, "invalid_product", "Informe o nome e um subsídio menor ou igual ao preço.");
        var id = existing ?? Guid.NewGuid();
        var args = new
        {
            id, actor.CompanyId, name, description = request.Description?.Trim() ?? "",
            request.Category, request.PriceCents, request.SubsidyCents, request.Available,
            request.ImageKey, barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim(), request.Version
        };
        await using var connection = await database.Open(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            Product? before = null;
            if (existing.HasValue)
            {
                before = await connection.QuerySingleOrDefaultAsync<Product>(Command($"""
                    SELECT {CheckoutService.ProductCols} FROM products WHERE id=@id AND company_id=@CompanyId FOR UPDATE
                    """, args, tx, ct)) ?? throw new BusinessError(404, "product_not_found", "Produto não encontrado.");
                if (before.Version != request.Version)
                    throw new BusinessError(409, "edit_conflict", "O produto foi alterado. Atualize o catálogo antes de editar.");
            }
            var sql = before is null ? """
                INSERT INTO products (id, company_id, name, description, category, price_cents, subsidy_cents, barcode, image_key, available)
                VALUES (@id,@CompanyId,@name,@description,@Category,@PriceCents,@SubsidyCents,@barcode,@ImageKey,@Available)
                """ : """
                UPDATE products SET name=@name, description=@description, category=@Category, price_cents=@PriceCents,
                    subsidy_cents=@SubsidyCents, barcode=@barcode, image_key=@ImageKey, available=@Available, version=version+1
                WHERE id=@id AND company_id=@CompanyId
                """;
            var product = await connection.QuerySingleAsync<Product>(Command(
                sql + " RETURNING " + CheckoutService.ProductCols, args, tx, ct));
            await Audit(connection, tx, actor, before is null ? "product_created" : "product_updated", id,
                System.Text.Json.JsonSerializer.Serialize(new { before, product }), ct);
            await tx.CommitAsync(ct);
            return product;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new BusinessError(409, "barcode_conflict", "O código de barras já está cadastrado.");
        }
    }

    public async Task<MealRule> SaveRule(Actor actor, RuleRequest request, CancellationToken ct)
    {
        if (request.Name.Trim().Length == 0 || request.StartsAt >= request.EndsAt || request.SubsidyCents > request.PriceCents)
            throw new BusinessError(422, "invalid_rule", "Confira o nome, o intervalo e o valor de subsídio.");
        await using var connection = await database.Open(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var before = await connection.QuerySingleAsync<MealRule>(Command(CheckoutService.RuleSql + " FOR UPDATE",
            new { actor.CompanyId }, tx, ct));
        if (before.Version != request.Version)
            throw new BusinessError(409, "edit_conflict", "A regra foi alterada. Atualize a tela antes de editar.");
        var args = new
        {
            actor.CompanyId, name = request.Name.Trim(), description = request.Description?.Trim() ?? "",
            request.StartsAt, request.EndsAt, request.PriceCents, request.SubsidyCents, request.MealLimit,
            request.DailyLimitCents, request.MonthlyLimitCents
        };
        await connection.ExecuteAsync(Command("""
            UPDATE meal_rules SET name=@name, description=@description, starts_at=@StartsAt, ends_at=@EndsAt,
                price_cents=@PriceCents, subsidy_cents=@SubsidyCents, meal_limit=@MealLimit,
                daily_limit_cents=@DailyLimitCents, monthly_limit_cents=@MonthlyLimitCents, version=version+1
            WHERE company_id=@CompanyId
            """, args, tx, ct));
        var rule = await connection.QuerySingleAsync<MealRule>(Command(CheckoutService.RuleSql, args, tx, ct));
        await Audit(connection, tx, actor, "meal_rule_updated", actor.CompanyId,
            System.Text.Json.JsonSerializer.Serialize(new { before, rule }), ct);
        await tx.CommitAsync(ct);
        return rule;
    }

    public async Task<Report> Report(Actor actor, DateOnly? from, DateOnly? to, int page, CancellationToken ct)
    {
        var (date, _) = Pricing.BusinessClock(clock.GetUtcNow(), actor.Timezone);
        var start = from ?? date;
        var end = to ?? date;
        if (start > end || end.DayNumber - start.DayNumber > 92 || page is < 1 or > 10000)
            throw new BusinessError(400, "invalid_period", "Selecione um período de até 93 dias e uma página válida.");
        await using var connection = await database.Open(ct);
        await using var tx = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        const string scope = "o.company_id=@CompanyId AND o.unit_id=@UnitId AND o.business_date BETWEEN @start AND @end";
        var args = new { actor.CompanyId, actor.UnitId, start, end, offset = (page - 1) * 30 };
        var summary = await connection.QuerySingleAsync<ReportSummary>(Command($"""
            SELECT COUNT(*) FILTER (WHERE status='confirmed') AS orders,
                COUNT(*) FILTER (WHERE status='confirmed' AND includes_meal) AS meals,
                COALESCE(SUM(subtotal_cents) FILTER (WHERE status='confirmed'),0)::bigint AS subtotal_cents,
                COALESCE(SUM(subsidy_cents) FILTER (WHERE status='confirmed'),0)::bigint AS subsidy_cents,
                COALESCE(SUM(total_cents) FILTER (WHERE status='confirmed'),0)::bigint AS total_cents,
                COUNT(*) FILTER (WHERE status='reversed') AS reversals
            FROM orders o WHERE {scope}
            """, args, tx, ct));
        var methods = await connection.QueryAsync<MethodTotal>(Command($"""
            SELECT p.method, COUNT(*) AS orders, SUM(p.amount_cents)::bigint AS total_cents
            FROM orders o JOIN payments p ON p.order_id=o.id WHERE {scope} AND o.status='confirmed'
            GROUP BY p.method ORDER BY p.method
            """, args, tx, ct));
        var orders = await connection.QueryAsync<ReportOrder>(Command($"""
            SELECT o.id, e.name AS employee, t.name AS terminal, o.created_at, o.business_date, p.methods[1] AS method,
                o.total_cents, o.status, p.reference, p.methods[2] AS additional_method
            FROM orders o JOIN employees e ON e.id=o.employee_id
                JOIN terminals t ON t.id=o.terminal_id
                JOIN LATERAL (
                    SELECT array_agg(method ORDER BY (method = 'payroll'), method) AS methods, MAX(reference) AS reference
                    FROM payments WHERE order_id=o.id
                ) p ON TRUE
            WHERE {scope}
            ORDER BY o.created_at DESC, o.id DESC LIMIT 30 OFFSET @offset
            """, args, tx, ct));
        await tx.CommitAsync(ct);
        return new(start, end, summary, methods.AsList(), orders.AsList(), page);
    }

    public async Task<IntegrationView> Integrations(Actor actor, CancellationToken ct)
    {
        await using var connection = await database.Open(ct);
        await using var tx = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        const string scope = """
            FROM outbox x JOIN orders o ON o.id=x.order_id WHERE o.company_id=@CompanyId AND o.unit_id=@UnitId
            """;
        var counts = await connection.QuerySingleAsync<QueueCount>(Command("""
            SELECT COUNT(*) FILTER (WHERE x.status='pending') AS pending,
                COUNT(*) FILTER (WHERE x.status='delivered') AS delivered, COUNT(*) FILTER (WHERE x.status='failed') AS failed
            """ + " " + scope, new { actor.CompanyId, actor.UnitId }, tx, ct));
        var events = await connection.QueryAsync<IntegrationEvent>(Command("""
            SELECT x.id,x.type,x.order_id,x.status,x.attempts,x.next_attempt_at,x.delivered_at,x.error_code
            """ + " " + scope + " ORDER BY o.created_at DESC, x.position DESC LIMIT 50",
            new { actor.CompanyId, actor.UnitId }, tx, ct));
        await tx.CommitAsync(ct);
        return new(config["Hr:Mode"]!, counts.Pending, counts.Delivered, counts.Failed, events.AsList());
    }

    public async Task Retry(Actor actor, Guid id, CancellationToken ct)
    {
        await using var connection = await database.Open(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var changed = await connection.ExecuteAsync(Command("""
            UPDATE outbox x SET status='pending', attempts=0, next_attempt_at=now(), error_code=NULL
            FROM orders o WHERE o.id=x.order_id AND x.id=@id AND x.status='failed' AND
                o.company_id=@CompanyId AND o.unit_id=@UnitId
            """, new { actor.CompanyId, actor.UnitId, id }, tx, ct));
        if (changed == 0) throw new BusinessError(409, "event_not_retryable", "O lançamento não está disponível para reprocessamento.");
        await Audit(connection, tx, actor, "integration_retried", id, null, ct);
        await tx.CommitAsync(ct);
    }

    private sealed record QueueCount(long Pending, long Delivered, long Failed);
}
