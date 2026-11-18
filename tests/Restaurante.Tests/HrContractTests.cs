
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Restaurante.Api.Domain;
using Restaurante.Api.Integration;

namespace Restaurante.Tests;

public sealed class HrContractTests
{
    [Fact]
    public async Task UsesClientCredentialsAndStableIdempotencyHeader()
    {
        var eventId = Guid.NewGuid();
        var handler = new ScriptedHttp(
            async request =>
            {
                Assert.Equal("https://rh.invalid/token", request.RequestUri!.AbsoluteUri);
                Assert.Contains("grant_type=client_credentials", await request.Content!.ReadAsStringAsync());
                return Json("""{"access_token":"ficticio","expires_in":60}""");
            },
            request =>
            {
                Assert.Equal("https://rh.invalid/api/employees/1001", request.RequestUri!.AbsoluteUri);
                Assert.Equal("Bearer ficticio", request.Headers.Authorization!.ToString());
                return Task.FromResult(Json("""{"active":true,"externalId":"sandbox-1001"}"""));
            },
            request =>
            {
                Assert.Equal("?date=2026-11-18&shift=LUNCH", request.RequestUri!.Query);
                return Task.FromResult(Json("""{"allowed":true,"payrollAllowed":true}"""));
            },
            async request =>
            {
                Assert.Equal("https://rh.invalid/api/payroll-deductions", request.RequestUri!.AbsoluteUri);
                Assert.Equal(eventId.ToString(), Assert.Single(request.Headers.GetValues("Idempotency-Key")));
                Assert.Equal("""{"totalCents":650}""", await request.Content!.ReadAsStringAsync());
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            });

        using var http = new HttpClient(handler);
        var adapter = new ExternalHr(http, new(new("https://rh.invalid/api/"), new("https://rh.invalid/token"),
            "test-client", "fictitious-test-secret", null), TimeProvider.System);
        var employee = new Employee(Guid.NewGuid(), "1001", "Marina", "Operações", "CLT", "sandbox-1001", true, true, true);
        Assert.Equal(new Eligibility(true, true), await adapter.Check(employee, new(2026, 11, 18), TestContextToken));
        var failure = await Assert.ThrowsAsync<HrProviderError>(() =>
            adapter.Deliver(eventId, "payroll", """{"totalCents":650}""", null!, null!, TestContextToken));
        Assert.True(failure.Retryable);
        Assert.Equal("http_503", failure.Code);
    }

    [Fact]
    public void RejectsNonHttpsIntegrationConfiguration()
    {
        var values = new Dictionary<string, string?> { ["Hr:BaseUrl"] = "http://rh.invalid/" };
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(values).Build();
        Assert.Throws<InvalidOperationException>(() => HrSettings.Read(configuration));
    }

    private static CancellationToken TestContextToken => CancellationToken.None;
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private sealed class ScriptedHttp(params Func<HttpRequestMessage, Task<HttpResponseMessage>>[] steps) : HttpMessageHandler
    {
        private int next;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => steps[next++](request);
    }
}
