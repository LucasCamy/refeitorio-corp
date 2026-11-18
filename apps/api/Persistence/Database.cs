
using Dapper;
using Npgsql;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Restaurante.Api.Domain;

namespace Restaurante.Api.Persistence;

public sealed class Database(NpgsqlDataSource dataSource)
{
    public ValueTask<NpgsqlConnection> Open(CancellationToken cancellationToken) =>
        dataSource.OpenConnectionAsync(cancellationToken);

    public async Task Migrate(bool sandbox, string? password, CancellationToken ct)
    {
        if (sandbox && (password is null || password.Length is < 12 or > 200))
            throw new InvalidOperationException("Configure Sandbox__Password (12 a 200 caracteres) para criar usuários fictícios.");

        await using var connection = await Open(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(Command("""
            SELECT pg_advisory_xact_lock(862634101);
            CREATE TABLE IF NOT EXISTS schema_migrations (name text PRIMARY KEY, checksum text NOT NULL);
            """, null, tx, ct));

        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.Contains(".Migrations.")).Order())
        {
            var sql = ReadResource(assembly, name);
            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
            var previous = await connection.QuerySingleOrDefaultAsync<string>(Command(
                "SELECT checksum FROM schema_migrations WHERE name = @name", new { name }, tx, ct));
            if (previous == checksum) continue;
            if (previous is not null) throw new InvalidOperationException($"Migration alterada após aplicação: {name}");
            await connection.ExecuteAsync(Command(sql, null, tx, ct));
            await connection.ExecuteAsync(Command("INSERT INTO schema_migrations VALUES (@name, @checksum)",
                new { name, checksum }, tx, ct));
        }

        if (sandbox)
        {
            var seed = assembly.GetManifestResourceNames().Single(name => name.EndsWith(".Sandbox.sql"));
            await connection.ExecuteAsync(Command(ReadResource(assembly, seed), null, tx, ct));

            (string Username, string Name, string Role, int Terminal)[] users =
            [
                ("operador", "Ana Ribeiro", "operator", 1),
                ("caixa2", "Pedro Martins", "operator", 2),
                ("gerente", "Beatriz Alves", "manager", 1),
                ("admin", "Juliana Melo", "admin", 1)
            ];
            var hasher = new PasswordHasher<string>();
            foreach (var user in users)
            {
                var hash = hasher.HashPassword(user.Username, password!);
                await connection.ExecuteAsync(Command("""
                    INSERT INTO staff_users (id,username,display_name,password_hash,role,terminal_id)
                    VALUES (@id,@Username,@Name,@hash,@Role,@terminal)
                    ON CONFLICT (username) DO NOTHING
                    """, new
                    {
                        id = Guid.NewGuid(), user.Username, user.Name, hash, user.Role,
                        terminal = Guid.Parse($"30000000-0000-4000-8000-{user.Terminal:D12}")
                    }, tx, ct));
            }
        }

        await tx.CommitAsync(ct);
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }

    public static CommandDefinition Command(string sql, object? parameters, NpgsqlTransaction? tx, CancellationToken ct) =>
        new(sql, parameters, tx, cancellationToken: ct);

    public static Task Audit(NpgsqlConnection connection, NpgsqlTransaction tx, Actor actor, string action,
        Guid? target, string? detail, CancellationToken ct) => connection.ExecuteAsync(Command("""
            INSERT INTO audit (company_id, terminal_id, actor_id, action, target_id, detail)
            VALUES (@CompanyId, @TerminalId, @ActorId, @action, @target, CAST(@detail AS jsonb))
            """, new { actor.CompanyId, actor.TerminalId, ActorId = actor.Id, action, target, detail }, tx, ct));
}
