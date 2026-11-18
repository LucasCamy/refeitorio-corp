
using Restaurante.Api.Domain;

namespace Restaurante.Tests;

public sealed class PricingTests
{
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Cart Cart = new(Guid.NewGuid(), true, [new(ProductId, 2)]);
    private static readonly DateOnly Date = new(2026, 11, 18);
    private static readonly TimeOnly Noon = new(12, 0);
    private static readonly MealRule Rule = new("Almoço", "", new(11, 0), new(14, 0), 1800, 1800, 1, 1000, 2000, 1);
    private static readonly Product Product = new(ProductId, "Suco", "", "beverage", 500, 100, null, null, true, 1);

    [Fact]
    public void AppliesMealAndPerItemSubsidiesInCents()
    {
        var quote = Price(new(0, 0, 0));
        Assert.Equal(2800, quote.SubtotalCents);
        Assert.Equal(2000, quote.SubsidyCents);
        Assert.Equal(800, quote.TotalCents);
        Assert.Equal(quote.Version, Price(new(0, 0, 0)).Version);
    }

    [Theory]
    [InlineData(200, 1200, 0, true)]
    [InlineData(201, 1200, 0, false)]
    [InlineData(0, 1201, 0, false)]
    [InlineData(0, 0, 1, false)]
    public void EnforcesLimitsIncludingExactBoundary(long daily, long monthly, int meals, bool allowed)
    {
        var error = Record.Exception(() => Price(new(daily, monthly, meals)));
        if (allowed) Assert.Null(error);
        else Assert.Equal(meals > 0 ? "duplicate_meal" : "spending_limit", Assert.IsType<BusinessError>(error).Code);
    }

    [Fact]
    public void UsesCompanyDateAndEndExclusive()
    {
        var (_, localTime) = Pricing.BusinessClock(new DateTimeOffset(2026, 11, 19, 1, 30, 0, TimeSpan.Zero), "America/Cuiaba");
        Assert.Equal(new TimeOnly(21, 30), localTime);
        Assert.Equal(Date, Pricing.BusinessClock(new DateTimeOffset(2026, 11, 19, 1, 30, 0, TimeSpan.Zero), "America/Cuiaba").Date);
        Assert.Null(Pricing.MealBlock(Rule, new(0, 0, 0), Rule.StartsAt));
        Assert.NotNull(Pricing.MealBlock(Rule, new(0, 0, 0), Rule.EndsAt));
    }

    [Fact]
    public void ExternalPaymentRequiresTheReferenceAndConfirmation()
    {
        var quote = Price(new(0, 0, 0));
        Assert.Throws<BusinessError>(() => Pricing.Payments(quote, "pix", true, null));
        Assert.Throws<BusinessError>(() => Pricing.Payments(quote, "card", false, "test"));
        Assert.Throws<BusinessError>(() => Pricing.Payments(quote, "payroll", false, null));
        Assert.Equal(new Payment("pix", 800, "test"), Assert.Single(Pricing.Payments(quote, "pix", true, "test")));
        Assert.Equal(new Payment("cash", 800, null), Assert.Single(Pricing.Payments(quote, "cash", true, null)));

        var mealRule = Rule with { SubsidyCents = 1200, DailyLimitCents = null };
        var mixed = Pricing.Calculate(Cart, mealRule, [Product], new(0, 0, 0), new(true, true), Date, Noon);
        Assert.Equal(600, mixed.MealPayrollCents);
        Assert.Equal(800, mixed.ExtrasAtRegisterCents);
        Assert.Equal([new Payment("card", 800, "ref"), new Payment("payroll", 600, null)],
            Pricing.Payments(mixed, "card", true, "ref"));
        Assert.Equal("payroll_required", Assert.Throws<BusinessError>(() =>
            Pricing.Calculate(Cart, mealRule, [Product], new(0, 0, 0), new(true, false), Date, Noon)).Code);
    }

    private static Quote Price(Usage usage) => Pricing.Calculate(Cart, Rule, [Product], usage, new(true, true), Date, Noon);
}
