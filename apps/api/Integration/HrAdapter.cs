
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using Restaurante.Api.Domain;
using Restaurante.Api.Persistence;

namespace Restaurante.Api.Integration;

public interface IHrAdapter
{
    Task<Eligibility> Check(Employee employee, DateOnly date, CancellationToken ct);
    Task Deliver(Guid eventId, string type, string payload, NpgsqlConnection connection, NpgsqlTransaction tx,
        CancellationToken ct);
}

public sealed class SandboxHr : IHrAdapter
{
    public Task<Eligibility> Check(Employee employee, DateOnly date, CancellationToken ct) =>
        Task.FromResult(new Eligibility(employee.Active && employee.Eligible, employee.PayrollAllowed));

    public Task Deliver(Guid eventId, string type, string payload, NpgsqlConnection connection, NpgsqlTransaction tx,
        CancellationToken ct) => connection.ExecuteAsync(Database.Command("""
            INSERT INTO sandbox_deliveries (id,type) VALUES (@eventId,@type) ON CONFLICT DO NOTHING
            """, new { eventId, type }, tx, ct));
}

public sealed record HrSettings(Uri BaseUrl, Uri TokenUrl, string ClientId, string ClientSecret, string? Scope)
{
    public static HrSettings Read(IConfiguration config)
    {
        Uri Https(string name) => Uri.TryCreate(config[name], UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 && uri.Query.Length == 0
            ? uri : throw new InvalidOperationException($"Configure uma URL HTTPS para {name}.");

        string Required(string name) => !string.IsNullOrWhiteSpace(config[name]) ? config[name]!
            : throw new InvalidOperationException($"Configure {name}.");

        var url = Https("Hr:BaseUrl");
        return new(new Uri(url.AbsoluteUri.TrimEnd('/') + "/"), Https("Hr:TokenUrl"),
            Required("Hr:ClientId"), Required("Hr:ClientSecret"), config["Hr:Scope"]);
    }
}

public sealed class HrProviderError(string code, bool retryable) : Exception(code)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}

public sealed class ExternalHr(HttpClient http, HrSettings settings, TimeProvider clock) : IHrAdapter
{
    private string? accessToken;
    private DateTimeOffset renewAt;
    private readonly SemaphoreSlim tokenGate = new(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Eligibility> Check(Employee employee, DateOnly date, CancellationToken ct)
    {
        if (!employee.Active || !employee.Eligible) return new(false, false);
        var path = "employees/" + Uri.EscapeDataString(employee.Rf);
        try
        {
            using var profile = await Send(HttpMethod.Get, path, null, null, ct);
            if (profile.StatusCode == HttpStatusCode.NotFound) return new(false, false);
            RequireSuccess(profile);
            var employeeProfile = await Parse<Profile>(profile, ct);
            if (!employeeProfile.Active || employeeProfile.ExternalId != employee.ExternalId) return new(false, false);

            using var benefit = await Send(HttpMethod.Get, $"{path}/eligibility?date={date:yyyy-MM-dd}&shift=LUNCH", null, null, ct);
            if (benefit.StatusCode == HttpStatusCode.NotFound) return new(false, false);
            RequireSuccess(benefit);
            var value = await Parse<Benefit>(benefit, ct);
            return new(value.Allowed, employee.PayrollAllowed && value.PayrollAllowed);
        }
        catch (HrProviderError)
        {
            throw new BusinessError(503, "hr_unavailable", "O RH está indisponível. Tente identificar novamente.");
        }
    }

    public async Task Deliver(Guid eventId, string type, string payload, NpgsqlConnection connection, NpgsqlTransaction tx,
        CancellationToken ct)
    {
        var path = type switch
        {
            "meal" => "meal-entries",
            "consumption" => "consumptions",
            "payroll" => "payroll-deductions",
            _ => throw new HrProviderError("unknown_event", false)
        };
        using var response = await Send(HttpMethod.Post, path, payload, eventId, ct);
        RequireSuccess(response);
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? payload, Guid? eventId,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(settings.BaseUrl, path));
        var token = await Token(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (eventId is Guid id) request.Headers.TryAddWithoutValidation("Idempotency-Key", id.ToString("D"));
        if (payload is not null) request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await SendHttp(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await tokenGate.WaitAsync(ct);
            try
            {
                if (accessToken == token) renewAt = DateTimeOffset.MinValue;
            }
            finally { tokenGate.Release(); }
        }
        return response;
    }

    private async Task<string> Token(CancellationToken ct)
    {
        await tokenGate.WaitAsync(ct);
        try
        {
            if (accessToken is not null && clock.GetUtcNow() < renewAt) return accessToken;
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret
            };
            if (settings.Scope is not null) form.Add("scope", settings.Scope);
            using var request = new HttpRequestMessage(HttpMethod.Post, settings.TokenUrl)
            { Content = new FormUrlEncodedContent(form) };
            using var response = await SendHttp(request, ct);
            RequireSuccess(response);
            var token = await Parse<OAuthToken>(response, ct);
            if (string.IsNullOrWhiteSpace(token.AccessToken) || token.ExpiresIn <= 0)
                throw new HrProviderError("invalid_token", false);
            accessToken = token.AccessToken;
            renewAt = clock.GetUtcNow().AddSeconds(Math.Clamp(token.ExpiresIn - 15, 0, 3600));
            return accessToken;
        }
        finally { tokenGate.Release(); }
    }

    private async Task<HttpResponseMessage> SendHttp(HttpRequestMessage request, CancellationToken ct)
    {
        try { return await http.SendAsync(request, ct); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new HrProviderError("timeout", true); }
        catch (HttpRequestException) { throw new HrProviderError("transport", true); }
    }

    private static async Task<T> Parse<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct) ??
                throw new HrProviderError("empty_response", false);
        }
        catch (JsonException) { throw new HrProviderError("invalid_contract", false); }
    }

    private static void RequireSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var status = (int)response.StatusCode;
        throw new HrProviderError($"http_{status}", status is 401 or 408 or 425 or 429 || status >= 500);
    }

    private sealed record Profile([property: System.Text.Json.Serialization.JsonRequired] bool Active,
        string ExternalId);
    private sealed record Benefit([property: System.Text.Json.Serialization.JsonRequired] bool Allowed,
        [property: System.Text.Json.Serialization.JsonRequired] bool PayrollAllowed);
    private sealed record OAuthToken(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);
}
