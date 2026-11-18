
using System.Security.Cryptography;
using System.Text.Json;

namespace Restaurante.Api.Domain;

public sealed record Actor(Guid Id, string Name, string Role, Guid CompanyId, Guid UnitId, string UnitName,
    Guid TerminalId, string TerminalName, string Timezone);
public sealed record Employee(Guid Id, string Rf, string Name, string Department, string Employment, string ExternalId,
    bool Active, bool Eligible, bool PayrollAllowed);
public sealed record EmployeeView(Guid Id, string Rf, string Name, string Department, string Employment);
public sealed record CheckoutSession(Guid EmployeeId, Guid OperatorId, Guid TerminalId, Guid Token, DateTime ExpiresAt);
public sealed record Identification(EmployeeView Employee, Guid CheckoutToken, DateTime ExpiresAt, bool PayrollAllowed,
    bool MealAvailable, string? MealMessage);
public sealed record Product(Guid Id, string Name, string Description, string Category, int PriceCents, int SubsidyCents,
    string? Barcode, string? ImageKey, bool Available, int Version);
public sealed record MealRule(string Name, string Description, TimeOnly StartsAt, TimeOnly EndsAt, int PriceCents,
    int SubsidyCents, int MealLimit, long? DailyLimitCents, long? MonthlyLimitCents, int Version);
public sealed record Catalog(IReadOnlyList<Product> Products, MealRule Rule, DateOnly BusinessDate,
    string Timezone, bool MealOpen);
public sealed record CartLine(Guid ProductId, int Quantity);
public sealed record Cart(Guid CheckoutToken, bool IncludesMeal, CartLine[] Items);
public sealed record Finalization(Guid CheckoutToken, bool IncludesMeal, CartLine[] Items, string QuoteVersion,
    string Method, bool Received, string? Reference);
public sealed record PricedLine(Guid? ProductId, string Name, int Quantity, int UnitPriceCents, int UnitSubsidyCents)
{
    public long SubtotalCents => (long)Quantity * UnitPriceCents;
    public long SubsidyCents => (long)Quantity * UnitSubsidyCents;
    [System.Text.Json.Serialization.JsonIgnore]
    public long NetCents => SubtotalCents - SubsidyCents;
}

public sealed record Quote(DateOnly BusinessDate, IReadOnlyList<PricedLine> Items, long SubtotalCents,
    long SubsidyCents, long TotalCents, string Version, bool PayrollAllowed)
{
    public long MealPayrollCents => Items.FirstOrDefault(item => item.ProductId is null)?.NetCents ?? 0;
    public long ExtrasAtRegisterCents => TotalCents - MealPayrollCents;
}
public sealed record Payment(string Method, long AmountCents, string? Reference);
public sealed record Receipt(Guid Id, EmployeeView Employee, DateTime CreatedAt, DateOnly BusinessDate,
    string Terminal, string Operator, string Status, IReadOnlyList<PricedLine> Items, long SubtotalCents,
    long SubsidyCents, long TotalCents, string Method, string? Reference, IReadOnlyList<Payment> Payments);
public sealed record SpendingTotals(long TotalCents, long PayrollCents, long RegisterCents);
public sealed record ExtraPurchase(Guid OrderId, int Position, string Name, int Quantity, long NetCents,
    DateTime CreatedAt, string Terminal);
public sealed record SpendingHistory(string Unit, SpendingTotals Today, SpendingTotals Month,
    IReadOnlyList<ExtraPurchase> Extras, bool HasMore);
public sealed record Usage(long DailyCents, long MonthlyCents, int Meals);
public sealed record Eligibility(bool Allowed, bool PayrollAllowed);
public sealed record ReportSummary(long Orders, long Meals, long SubtotalCents, long SubsidyCents,
    long TotalCents, long Reversals);
public sealed record MethodTotal(string Method, long Orders, long TotalCents);
public sealed record ReportOrder(Guid Id, string Employee, string Terminal, DateTime CreatedAt, DateOnly BusinessDate,
    string Method, long TotalCents, string Status, string? Reference, string? AdditionalMethod)
{
    public string[] PaymentMethods => AdditionalMethod is null ? [Method] : [Method, AdditionalMethod];
}
public sealed record Report(DateOnly From, DateOnly To, ReportSummary Summary, IReadOnlyList<MethodTotal> Methods,
    IReadOnlyList<ReportOrder> Orders, int Page);
public sealed record IntegrationEvent(Guid Id, string Type, Guid OrderId, string Status, int Attempts,
    DateTime NextAttemptAt, DateTime? DeliveredAt, string? ErrorCode);
public sealed record IntegrationView(string Mode, long Pending, long Delivered, long Failed,
    IReadOnlyList<IntegrationEvent> Events);
public sealed record HrPayload(Guid TransactionId, string EmployeeExternalId, DateOnly Date, string Shift,
    long TotalCents, long SubsidyCents, IReadOnlyList<PricedLine> Items, bool Reversal);
public sealed class BusinessError(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public static class Pricing
{
    public const string PayrollRequired = "O almoço exige habilitação para desconto em folha. Os extras podem ser pagos no caixa.";

    public static (DateOnly Date, TimeOnly Time) BusinessClock(DateTimeOffset now, string timezone)
    {
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(timezone));
        return (DateOnly.FromDateTime(local.DateTime), TimeOnly.FromDateTime(local.DateTime));
    }

    public static string? MealBlock(MealRule rule, Usage usage, TimeOnly time) => (time, usage.Meals) switch
    {
        _ when time < rule.StartsAt || time >= rule.EndsAt => "O almoço está fora do horário configurado.",
        _ when usage.Meals >= rule.MealLimit => "O limite de almoços deste dia já foi alcançado.",
        _ => null
    };

    public static void CheckCart(Cart cart)
    {
        if (cart.CheckoutToken == Guid.Empty || cart.Items is null || cart.Items.Length > 30 ||
            cart.Items.Any(i => i is null || i.ProductId == Guid.Empty || i.Quantity is < 1 or > 20) ||
            cart.Items.Select(i => i.ProductId).Distinct().Count() != cart.Items.Length ||
            (!cart.IncludesMeal && cart.Items.Length == 0))
            throw new BusinessError(400, "invalid_cart", "Selecione o almoço ou até 30 produtos, de 1 a 20 unidades por produto.");
    }

    public static Quote Calculate(Cart cart, MealRule rule, IReadOnlyList<Product> products, Usage usage,
        Eligibility eligibility, DateOnly date, TimeOnly time)
    {
        CheckCart(cart);
        if (!eligibility.Allowed)
            throw new BusinessError(403, "employee_ineligible", "O funcionário não está elegível para atendimento.");
        var lines = new List<PricedLine>();
        if (cart.IncludesMeal)
        {
            var block = MealBlock(rule, usage, time);
            if (block is not null)
                throw new BusinessError(409, usage.Meals >= rule.MealLimit ? "duplicate_meal" : "meal_closed", block);
            if (rule.PriceCents > rule.SubsidyCents && !eligibility.PayrollAllowed)
                throw new BusinessError(422, "payroll_required", PayrollRequired);
            lines.Add(new(null, rule.Name, 1, rule.PriceCents, rule.SubsidyCents));
        }

        var available = products.Where(p => p.Available).ToDictionary(p => p.Id);
        foreach (var input in cart.Items.OrderBy(item => item.ProductId))
        {
            if (!available.TryGetValue(input.ProductId, out var product))
                throw new BusinessError(409, "product_unavailable", "Um item deixou de estar disponível. Atualize o pedido.");
            lines.Add(new(product.Id, product.Name, input.Quantity, product.PriceCents, product.SubsidyCents));
        }

        var subtotal = lines.Sum(line => line.SubtotalCents);
        var subsidy = lines.Sum(line => line.SubsidyCents);
        var total = subtotal - subsidy;
        if (total > 0 && (usage.DailyCents + total > rule.DailyLimitCents || usage.MonthlyCents + total > rule.MonthlyLimitCents))
            throw new BusinessError(409, "spending_limit", "O pedido ultrapassa o limite diário ou mensal do funcionário.");
        var version = Hash(new { cart.CheckoutToken, date, lines, eligibility.PayrollAllowed, policy = "meal_payroll_extras_register" });
        return new(date, lines, subtotal, subsidy, total, version, eligibility.PayrollAllowed);
    }

    public static IReadOnlyList<Payment> Payments(Quote quote, string method, bool received, string? reference)
    {
        if (quote.MealPayrollCents > 0 && !quote.PayrollAllowed)
            throw new BusinessError(422, "payroll_required", PayrollRequired);
        if (quote.TotalCents == 0 && method == "subsidy") return [new("subsidy", 0, null)];
        if (quote.MealPayrollCents > 0 && quote.ExtrasAtRegisterCents == 0 && method == "payroll")
            return [new("payroll", quote.MealPayrollCents, null)];
        if (quote.ExtrasAtRegisterCents <= 0 || !received ||
            !(method == "cash" || ((method is "card" or "pix") && !string.IsNullOrWhiteSpace(reference))))
            throw new BusinessError(422, "invalid_payment", "Confirme o pagamento dos extras no caixa. Cartão e PIX exigem a referência da operação.");

        var extras = new Payment(method, quote.ExtrasAtRegisterCents, reference);
        return quote.MealPayrollCents > 0 ? [extras, new("payroll", quote.MealPayrollCents, null)] : [extras];
    }

    public static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
