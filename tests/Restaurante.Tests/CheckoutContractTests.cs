
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Restaurante.Api.Contracts;
using Restaurante.Api.Domain;
using Restaurante.Api.Integration;
using Restaurante.Api.Persistence;

namespace Restaurante.Tests;

public sealed class CheckoutContractTests : IAsyncLifetime
{
    private readonly string dbName = "rf_test_" + Guid.NewGuid().ToString("N");
    private readonly string password = Guid.NewGuid().ToString("N");
    private WebApplicationFactory<Program>? factory;
    private string? adminConnection;

    public async Task InitializeAsync()
    {
        var config = Environment.GetEnvironmentVariable("ConnectionStrings__Refeitorio")
            ?? throw new InvalidOperationException("Execute scripts/dev.ps1 test com PostgreSQL local.");
        var target = new NpgsqlConnectionStringBuilder(config);
        if (target.Host is not ("localhost" or "127.0.0.1") || target.Database != "refeitorio")
            throw new InvalidOperationException("Use a instância PostgreSQL de sandbox para os testes.");
        target.Database = "postgres";
        adminConnection = target.ConnectionString;
        await Execute(adminConnection, "CREATE DATABASE " + dbName);
        target.Database = dbName;
        await using (var dataSource = NpgsqlDataSource.Create(target.ConnectionString))
            await new Database(dataSource).Migrate(true, password, CancellationToken.None);

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Development").UseSetting("Hr:Mode", "Sandbox")
                .UseSetting("ConnectionStrings:Refeitorio", target.ConnectionString);
            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHrAdapter>();
                services.AddSingleton<IHrAdapter, IntermittentSandbox>();
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync();
        if (adminConnection is not null) await Execute(adminConnection, "DROP DATABASE IF EXISTS " + dbName + " WITH (FORCE)");
    }

    [Fact]
    public async Task ConcurrencyPaymentReversalAndOutboxShareOneContract()
    {
        using var anonymous = factory!.CreateClient();
        using var admin = await Login("admin");
        using var first = await Login("operador");
        using var second = await Login("caixa2");
        using var manager = await Login("gerente");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await first.GetAsync("/api/v1/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/v1/integration/events")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await first.PostAsJsonAsync("/api/v1/checkout/identify", new { kind = "rf", value = "1003" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/v1/checkout/identify", new { kind = "unknown", value = "1001" })).StatusCode);

        var product = await Read<Product>(await admin.PostAsJsonAsync("/api/v1/products",
            new ProductRequest("Café de teste", "150 ml", "beverage", 650, 0, "CFE-TESTE", null, true, null)));
        var setup = (await Read<Catalog>(await admin.GetAsync("/api/v1/checkout/catalog"))).Rule;
        await Read<MealRule>(await admin.PutAsJsonAsync("/api/v1/meal-rule",
            new RuleRequest(setup.Name, setup.Description, setup.StartsAt, setup.EndsAt, setup.PriceCents, 1200,
                setup.MealLimit, setup.DailyLimitCents, setup.MonthlyLimitCents, setup.Version)));
        var invalidEdit = new ProductRequest("Café", "", "beverage", 650, 0, null, null, true, 99);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync("/api/v1/products/" + product.Id, invalidEdit)).StatusCode);
        var competitor = await Task.WhenAll(IdentifyRf(first), IdentifyRf(second));
        Assert.Single(competitor, item => item.StatusCode == HttpStatusCode.Conflict);
        var winningIndex = competitor[0].IsSuccessStatusCode ? 0 : 1;
        var winner = winningIndex == 0 ? first : second;
        var loser = winningIndex == 0 ? second : first;
        var identified = await Read<Identification>(competitor[winningIndex]);
        Assert.True(identified.MealAvailable);
        var cart = new Cart(identified.CheckoutToken, true, [new(product.Id, 1)]);
        var quote = await Read<Quote>(await winner.PostAsJsonAsync("/api/v1/checkout/quote", cart));
        Assert.Equal(1250, quote.TotalCents);
        Assert.Equal(600, quote.MealPayrollCents);
        Assert.Equal(650, quote.ExtrasAtRegisterCents);

        var finalization = new Finalization(cart.CheckoutToken, true, cart.Items, quote.Version, "cash", true, null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await Finalize(winner, Guid.NewGuid(), finalization with { Method = "payroll" })).StatusCode);
        var key = Guid.NewGuid();
        var paid = await Task.WhenAll(Finalize(winner, key, finalization), Finalize(winner, key, finalization));
        var firstReceipt = await Read<Receipt>(paid[0]);
        var duplicate = await Read<Receipt>(paid[1]);
        Assert.Equal(firstReceipt.Id, duplicate.Id);
        Assert.Equal(1250, firstReceipt.TotalCents);
        Assert.Equal([new Payment("cash", 650, null), new Payment("payroll", 600, null)], firstReceipt.Payments);
        Assert.Equal(HttpStatusCode.Conflict, (await Finalize(winner, key, finalization with { Method = "pix" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await loser.GetAsync("/api/v1/orders/" + firstReceipt.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await winner.PostAsJsonAsync("/api/v1/orders/" + firstReceipt.Id + "/reverse",
            new { reason = "Motivo de teste" })).StatusCode);

        var next = await Read<Identification>(await IdentifyRf(loser));
        Assert.False(next.MealAvailable);
        var historyUrl = $"/api/v1/checkout/sessions/{next.CheckoutToken}/spending";
        Assert.Equal(HttpStatusCode.Conflict, (await winner.GetAsync(historyUrl)).StatusCode);
        var history = await Read<SpendingHistory>(await loser.GetAsync(historyUrl));
        Assert.Equal(new SpendingTotals(1250, 600, 650), history.Today);
        Assert.Equal(history.Today, history.Month);
        Assert.Equal("Café de teste", Assert.Single(history.Extras).Name);
        Assert.Equal(650, Assert.Single(history.Extras).NetCents);
        Assert.False(history.HasMore);
        var blocked = await loser.PostAsJsonAsync("/api/v1/checkout/quote", cart with { CheckoutToken = next.CheckoutToken });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("duplicate_meal", await blocked.Content.ReadAsStringAsync());
        var report = await Read<Report>(await manager.GetAsync("/api/v1/reports/orders"));
        Assert.Equal(1, report.Summary.Orders);
        Assert.Equal(1, report.Summary.Meals);
        Assert.Equal(600, Assert.Single(report.Methods, m => m.Method == "payroll").TotalCents);
        Assert.Equal(650, Assert.Single(report.Methods, m => m.Method == "cash").TotalCents);
        Assert.Equal(["cash", "payroll"], Assert.Single(report.Orders).PaymentMethods);

        var reversed = await Read<Receipt>(await manager.PostAsJsonAsync("/api/v1/orders/" + firstReceipt.Id + "/reverse",
            new ReverseRequest("Operação de teste", "DEV-TESTE")));
        Assert.Equal("reversed", reversed.Status);
        Assert.True((await Read<Identification>(await IdentifyRf(loser))).MealAvailable);
        var historyAfterReversal = await Read<SpendingHistory>(await loser.GetAsync(historyUrl));
        Assert.Equal(new SpendingTotals(0, 0, 0), historyAfterReversal.Month);
        Assert.Empty(historyAfterReversal.Extras);

        IntegrationView? status = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            status = await Read<IntegrationView>(await admin.GetAsync("/api/v1/integration/events"));
            if (status.Delivered == 6) break;
            await Task.Delay(600);
        }
        Assert.NotNull(status);
        Assert.Equal(6, status.Delivered);
        Assert.Equal(0, status.Pending);
        Assert.Contains(status.Events, e => e.Type == "payroll" && e.Attempts > 1);
        var sandbox = (IntermittentSandbox)factory.Services.GetRequiredService<IHrAdapter>();
        Assert.Equal([600L, -600L], sandbox.PayrollAmounts.ToArray());

        using var contract = JsonDocument.Parse(await anonymous.GetStringAsync("/openapi/v1.json"));
        Assert.True(contract.RootElement.GetProperty("paths").TryGetProperty("/api/v1/orders", out var orders));
        Assert.True(orders.GetProperty("post").TryGetProperty("security", out _));

        Task<HttpResponseMessage> IdentifyRf(HttpClient client) =>
            client.PostAsJsonAsync("/api/v1/checkout/identify", new IdentifyRequest("rf", "1001"));
    }

    [Fact]
    public async Task BadgeAndCurrentPricesAreCheckedBeforeRecordingExternalPayment()
    {
        using var admin = await Login("admin");
        using var operatorClient = await Login("operador");
        var badge = await Read<Identification>(await operatorClient.PostAsJsonAsync("/api/v1/checkout/identify",
            new IdentifyRequest("badge", "RF-BADGE-77a64bab-fb5b-449b-9e48-931c02355b80")));
        Assert.Equal("1001", badge.Employee.Rf);
        Assert.Equal(HttpStatusCode.NoContent, (await operatorClient.DeleteAsync("/api/v1/checkout/sessions/" + badge.CheckoutToken)).StatusCode);

        var identified = await Read<Identification>(await operatorClient.PostAsJsonAsync("/api/v1/checkout/identify",
            new IdentifyRequest("rf", "1004")));
        Assert.False(identified.PayrollAllowed);
        var before = await Read<Catalog>(await admin.GetAsync("/api/v1/checkout/catalog"));
        var product = Assert.Single(before.Products, item => item.ImageKey == "juice");
        var cart = new Cart(identified.CheckoutToken, false, [new(product.Id, 1)]);
        var quote = await Read<Quote>(await operatorClient.PostAsJsonAsync("/api/v1/checkout/quote", cart));
        var proposed = new ProductRequest(product.Name, product.Description, product.Category,
            product.PriceCents + 50, product.SubsidyCents, product.Barcode, product.ImageKey, true, product.Version);
        await Read<Product>(await admin.PutAsJsonAsync("/api/v1/products/" + product.Id, proposed));

        var stale = new Finalization(cart.CheckoutToken, false, cart.Items, quote.Version, "cash", true, null);
        var conflict = await Finalize(operatorClient, Guid.NewGuid(), stale);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains("quote_changed", await conflict.Content.ReadAsStringAsync());

        quote = await Read<Quote>(await operatorClient.PostAsJsonAsync("/api/v1/checkout/quote", cart));
        var payment = stale with { QuoteVersion = quote.Version, Method = "card" };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Finalize(operatorClient, Guid.NewGuid(), payment)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Finalize(operatorClient, Guid.NewGuid(), payment with { Method = "payroll" })).StatusCode);
        var receipt = await Read<Receipt>(await Finalize(operatorClient, Guid.NewGuid(), payment with { Reference = "NSU-052918" }));
        Assert.Equal("NSU-052918", receipt.Reference);
        Assert.Equal(proposed.PriceCents, receipt.TotalCents);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/v1/orders/{receipt.Id}/reverse",
            new ReverseRequest("Estorno sem referência", null))).StatusCode);
    }

    private async Task<HttpClient> Login(string username)
    {
        var client = factory!.CreateClient();
        var login = await Read<LoginResponse>(await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(username, password)));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private static async Task<HttpResponseMessage> Finalize(HttpClient client, Guid key, Finalization finalization)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders") { Content = JsonContent.Create(finalization) };
        req.Headers.Add("Idempotency-Key", key.ToString());
        return await client.SendAsync(req);
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task Execute(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var command = new NpgsqlCommand(sql, conn);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class IntermittentSandbox : IHrAdapter
    {
        private readonly SandboxHr sandbox = new();
        private int failures;
        public System.Collections.Concurrent.ConcurrentQueue<long> PayrollAmounts { get; } = new();
        public Task<Eligibility> Check(Employee employee, DateOnly date, CancellationToken ct) => sandbox.Check(employee, date, ct);
        public Task Deliver(Guid eventId, string type, string payload, NpgsqlConnection connection, NpgsqlTransaction tx,
            CancellationToken ct)
        {
            if (type == "payroll")
            {
                if (Interlocked.Increment(ref failures) == 1) throw new HrProviderError("test_timeout", true);
                using var content = JsonDocument.Parse(payload);
                PayrollAmounts.Enqueue(content.RootElement.GetProperty("totalCents").GetInt64());
            }
            return sandbox.Deliver(eventId, type, payload, connection, tx, ct);
        }
    }
}
