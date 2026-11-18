
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Restaurante.Api.Domain;
using Restaurante.Api.Persistence;
using static Restaurante.Api.Persistence.Database;

namespace Restaurante.Api.Contracts;

public sealed record TokenSettings(SymmetricSecurityKey Key, TimeSpan Lifetime)
{
    public const string Issuer = "refeitorio-api";
    public const string Audience = "refeitorio-pos";

    public static TokenSettings Read(IConfiguration configuration, bool sandbox)
    {
        var value = configuration["Jwt:SigningKey"];
        var bytes = value is not null ? Convert.FromBase64String(value) :
            sandbox ? RandomNumberGenerator.GetBytes(48) : [];
        if (bytes.Length < 32)
            throw new InvalidOperationException("Configure Jwt__SigningKey em Base64 com pelo menos 32 bytes aleatórios.");
        return new(new SymmetricSecurityKey(bytes), TimeSpan.FromMinutes(45));
    }
}

public sealed class StaffAuthentication(Database database, TokenSettings settings, TimeProvider time, IWebHostEnvironment environment,
    IConfiguration config)
{
    private const string StaffCols = """
        s.id, s.display_name AS name, s.role, c.id AS company_id, u.id AS unit_id, u.name AS unit_name,
        t.id AS terminal_id, t.name AS terminal_name, c.timezone
        """;
    private const string Scope = """
        FROM staff_users s JOIN terminals t ON t.id=s.terminal_id JOIN units u ON u.id=t.unit_id
            JOIN companies c ON c.id=u.company_id WHERE s.active
        """;
    private static readonly PasswordHasher<string> Hasher = new();
    private static readonly string DummyHash = Hasher.HashPassword("", Guid.NewGuid().ToString("N"));

    public async Task<LoginResponse> Login(LoginRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim().ToLowerInvariant();
        await using var connection = await database.Open(ct);
        var staff = await connection.QuerySingleOrDefaultAsync<StaffCredential>(Command(
            $"SELECT {StaffCols}, s.password_hash {Scope} AND s.username=@username", new { username }, null, ct));
        var valid = Hasher.VerifyHashedPassword(username, staff?.PasswordHash ?? DummyHash, request.Password) !=
            PasswordVerificationResult.Failed;
        if (!valid || staff is null)
            throw new BusinessError(401, "invalid_credentials", "Usuário ou senha inválidos.");

        var actor = staff.Actor;
        var now = time.GetUtcNow().UtcDateTime;
        var claims = new Claim[]
        {
            new(JwtRegisteredClaimNames.Sub, actor.Id.ToString("D")),
            new("role", actor.Role), new("terminal", actor.TerminalId.ToString("D")), new("unit", actor.UnitId.ToString("D")),
            new("company", actor.CompanyId.ToString("D")), new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        var expires = now.Add(settings.Lifetime);
        var token = new JwtSecurityToken(TokenSettings.Issuer, TokenSettings.Audience, claims, now, expires,
            new SigningCredentials(settings.Key, SecurityAlgorithms.HmacSha256));
        await using var tx = await connection.BeginTransactionAsync(ct);
        await Audit(connection, tx, actor, "staff_logged_in", actor.Id, null, ct);
        await tx.CommitAsync(ct);
        return new(new JwtSecurityTokenHandler().WriteToken(token), expires, actor,
            environment.IsDevelopment() && config["Hr:Mode"] == "Sandbox");
    }

    public async Task<Actor?> ActiveActor(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)) return null;
        await using var connection = await database.Open(ct);
        var actor = await connection.QuerySingleOrDefaultAsync<Actor>(Command(
            $"SELECT {StaffCols} {Scope} AND s.id=@id", new { id }, null, ct));
        return actor is not null &&
            actor.Role == principal.FindFirstValue("role") &&
            actor.CompanyId.ToString("D") == principal.FindFirstValue("company") &&
            actor.UnitId.ToString("D") == principal.FindFirstValue("unit") &&
            actor.TerminalId.ToString("D") == principal.FindFirstValue("terminal") ? actor : null;
    }

    private sealed record StaffCredential(Guid Id, string Name, string Role, Guid CompanyId, Guid UnitId, string UnitName,
        Guid TerminalId, string TerminalName, string Timezone, string PasswordHash)
    {
        public Actor Actor => new(Id, Name, Role, CompanyId, UnitId, UnitName, TerminalId, TerminalName, Timezone);
    }
}
