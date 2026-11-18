
using System.Text.Json;
using Dapper;
using Npgsql;
using Restaurante.Api.Domain;
using Restaurante.Api.Persistence;
using static Restaurante.Api.Persistence.Database;

namespace Restaurante.Api.Integration;

public sealed class OutboxWorker(Database database, IHrAdapter hr, TimeProvider clock, ILogger<OutboxWorker> logger)
    : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task Enqueue(NpgsqlConnection connection, NpgsqlTransaction tx, HrPayload payload,
        bool includesMeal, long payrollCents, CancellationToken ct)
    {
        var kinds = new List<string>();
        if (includesMeal) kinds.Add("meal");
        if (payload.Items.Any(item => item.ProductId is not null)) kinds.Add("consumption");
        if (payrollCents != 0) kinds.Add("payroll");

        for (var i = 0; i < kinds.Count; i++)
        {
            var type = kinds[i];
            // Each contract receives only the lines it uses.
            var items = type switch
            {
                "meal" => payload.Items.Where(item => item.ProductId is null).ToArray(),
                "consumption" => payload.Items.Where(item => item.ProductId is not null).ToArray(),
                _ => []
            };
            var subset = payload with
            {
                Items = items,
                TotalCents = type == "payroll" ? payrollCents : items.Sum(x => x.NetCents) *
                    (payload.Reversal ? -1 : 1),
                SubsidyCents = type == "payroll" ? 0 : items.Sum(x => x.SubsidyCents) * (payload.Reversal ? -1 : 1)
            };
            await connection.ExecuteAsync(Command("""
                INSERT INTO outbox (id,order_id,position,type,payload)
                VALUES (@id,@orderId,@position,@type,CAST(@body AS jsonb))
                """, new { id = Guid.NewGuid(), orderId = payload.TransactionId,
                    position = (payload.Reversal ? 100 : 0) + i, type, body = JsonSerializer.Serialize(subset, JsonOptions) }, tx, ct));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await ProcessOne(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("O processamento da fila foi interrompido por {FailureType}.", ex.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task<bool> ProcessOne(CancellationToken ct)
    {
        await using var connection = await database.Open(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<Pending>(Command("""
            SELECT x.id, x.type, x.payload::text AS payload, x.attempts
            FROM outbox x
            WHERE x.status = 'pending' AND x.next_attempt_at <= now()
                AND NOT EXISTS (
                    SELECT 1 FROM outbox p
                    WHERE p.order_id = x.order_id AND p.position < x.position AND p.status <> 'delivered'
                )
            ORDER BY x.next_attempt_at, x.id
            FOR UPDATE OF x SKIP LOCKED LIMIT 1
            """, null, tx, ct));
        if (row is null) return false;
        var attempts = row.Attempts + 1;

        try
        {
            await hr.Deliver(row.Id, row.Type, row.Payload, connection, tx, ct);
            await connection.ExecuteAsync(Command("""
                UPDATE outbox SET status = 'delivered', attempts = @attempts, delivered_at = now(), error_code = NULL
                WHERE id = @Id
                """, new { row.Id, attempts }, tx, ct));
        }
        catch (HrProviderError ex)
        {
            var status = ex.Retryable && attempts < 8 ? "pending" : "failed";
            var next = clock.GetUtcNow().UtcDateTime.AddSeconds(Math.Min(900, 5 * (1 << Math.Min(attempts - 1, 8))));
            await connection.ExecuteAsync(Command("""
                UPDATE outbox SET status = @status, attempts = @attempts, next_attempt_at = @next, error_code = @Code
                WHERE id = @Id
                """, new { row.Id, attempts, status, next, ex.Code }, tx, ct));
            logger.LogWarning("Entrega de evento {EventId}: {Code}, tentativa {Attempts}.", row.Id, ex.Code, attempts);
        }
        await tx.CommitAsync(ct);
        return true;
    }

    private sealed record Pending(Guid Id, string Type, string Payload, int Attempts);
}
