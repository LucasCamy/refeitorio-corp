
using System.ComponentModel.DataAnnotations;
using Restaurante.Api.Domain;

namespace Restaurante.Api.Contracts;

public sealed record LoginRequest([Required, StringLength(80)] string Username,
    [Required, StringLength(200)] string Password);
public sealed record IdentifyRequest([RegularExpression("rf|badge"), Required] string Kind,
    [Required, StringLength(128)] string Value);
public sealed record ProductRequest([Required, StringLength(80)] string Name,
    [StringLength(200)] string? Description, [RegularExpression("beverage|dessert|meal|other"), Required] string Category,
    [Range(0, 1000000)] int PriceCents, [Range(0, 1000000)] int SubsidyCents, [StringLength(40)] string? Barcode,
    [RegularExpression("juice|cola|flan|brownie|takeout")] string? ImageKey, bool Available, int? Version);
public sealed record RuleRequest([Required, StringLength(80)] string Name,
    [StringLength(200)] string? Description, TimeOnly StartsAt, TimeOnly EndsAt,
    [Range(0, 1000000)] int PriceCents, [Range(0, 1000000)] int SubsidyCents, [Range(1, 10)] int MealLimit,
    [Range(0, 100000000)] long? DailyLimitCents, [Range(0, 100000000)] long? MonthlyLimitCents, [Range(1, int.MaxValue)] int Version);
public sealed record ReverseRequest([Required, StringLength(300, MinimumLength = 8)] string Reason,
    [StringLength(100)] string? RefundReference);
public sealed record LoginResponse(string AccessToken, DateTime ExpiresAt, Actor Staff, bool Sandbox);
