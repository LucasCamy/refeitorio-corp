
using System.Data;
using System.Threading.RateLimiting;
using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Restaurante.Api.Contracts;
using Restaurante.Api.Domain;
using Restaurante.Api.Integration;
using Restaurante.Api.Persistence;

DefaultTypeMap.MatchNamesWithUnderscores = true;
SqlMapper.AddTypeHandler(new PostgreSqlDate());
SqlMapper.AddTypeHandler(new PostgreSqlTime());
var migration = args.Contains("--migrate", StringComparer.Ordinal);
var seed = args.Contains("--seed", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(arg => arg is not ("--migrate" or "--seed")).ToArray());
var hrMode = builder.Configuration["Hr:Mode"];
if (hrMode is not ("Sandbox" or "External"))
    throw new InvalidOperationException("Configure Hr__Mode como Sandbox ou External.");
var sandbox = hrMode == "Sandbox";
if ((sandbox || seed) && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Sandbox disponível somente com ASPNETCORE_ENVIRONMENT=Development.");
if (seed && (!migration || !sandbox))
    throw new InvalidOperationException("Use --migrate --seed em sandbox para preparar o banco.");

var connectionString = builder.Configuration.GetConnectionString("Refeitorio") ??
    throw new InvalidOperationException("Configure ConnectionStrings__Refeitorio.");
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<Database>();
if (migration)
{
    await using var migrationApp = builder.Build();
    await migrationApp.Services.GetRequiredService<Database>()
        .Migrate(seed, builder.Configuration["Sandbox:Password"], CancellationToken.None);
    Console.WriteLine(seed ? "Banco migrado e dados fictícios preparados." : "Banco migrado.");
    return;
}

var token = TokenSettings.Read(builder.Configuration, sandbox);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 32768);
builder.Services.AddSingleton(token);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<StaffAuthentication>();
builder.Services.AddSingleton<CheckoutService>();
builder.Services.AddSingleton<ManagementService>();
if (sandbox) builder.Services.AddSingleton<IHrAdapter, SandboxHr>();
else
{
    builder.Services.AddSingleton(HrSettings.Read(builder.Configuration));
    builder.Services.AddHttpClient("rh", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(4);
        client.MaxResponseContentBufferSize = 262144;
    }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    });
    builder.Services.AddSingleton<IHrAdapter>(services => new ExternalHr(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("rh"),
        services.GetRequiredService<HrSettings>(), services.GetRequiredService<TimeProvider>()));
}
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer<OpenApiSecurity>();
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new()
    {
        ValidateIssuer = true, ValidIssuer = TokenSettings.Issuer,
        ValidateAudience = true, ValidAudience = TokenSettings.Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = token.Key,
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(5),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], RoleClaimType = "role"
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var actor = await context.HttpContext.RequestServices.GetRequiredService<StaffAuthentication>()
                .ActiveActor(context.Principal!, context.HttpContext.RequestAborted);
            if (actor is null) context.Fail("Sessão inválida.");
            else context.HttpContext.Items[typeof(Actor)] = actor;
        }
    };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("staff", p => p.RequireRole("operator", "manager", "admin"))
    .AddPolicy("management", p => p.RequireRole("manager", "admin"))
    .AddPolicy("admin", p => p.RequireRole("admin"));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 12, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("staff", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 180, Window = TimeSpan.FromMinutes(1) }));
    options.OnRejected = (context, _) => new ValueTask(Results.Problem(statusCode: 429,
        title: "Muitas requisições. Aguarde um momento.").ExecuteAsync(context.HttpContext));
});

var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async ctx =>
{
    var error = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var result = error switch
    {
        BusinessError e => Results.Problem(statusCode: e.Status, title: e.Message, extensions: new Dictionary<string, object?> { ["code"] = e.Code }),
        BadHttpRequestException e => Results.Problem(statusCode: e.StatusCode, title: "A requisição é inválida."),
        _ => Results.Problem(statusCode: 500, title: "Não foi possível concluir a operação.")
    };
    await result.ExecuteAsync(ctx);
}));
app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;
    await Results.Problem(statusCode: response.StatusCode, title: response.StatusCode switch
    {
        401 => "Entre novamente para continuar.",
        403 => "Seu perfil não tem acesso a esta operação.",
        _ => "Não foi possível atender a requisição."
    }).ExecuteAsync(context.HttpContext);
});
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/healthz", async (Database database, CancellationToken ct) =>
{
    try { await using var connection = await database.Open(ct); return Results.Ok(new { status = "ok" }); }
    catch (NpgsqlException) { return Results.StatusCode(503); }
}).ExcludeFromDescription();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Refeitório Inteligente v1"));
}

var v1 = app.MapGroup("/api/v1");
v1.MapPost("/auth/login", (LoginRequest body, StaffAuthentication auth, CancellationToken ct) => auth.Login(body, ct))
    .RequireRateLimiting("login").WithTags("Sessão").ProducesProblem(401).ProducesValidationProblem();
var staff = v1.MapGroup("").RequireAuthorization("staff").RequireRateLimiting("staff").ProducesProblem(401).ProducesProblem(403);
staff.MapGet("/checkout/catalog", (HttpContext context, CheckoutService service, CancellationToken ct) =>
    service.Catalog(ActorOf(context), ct)).WithTags("Caixa");
staff.MapPost("/checkout/identify", (IdentifyRequest body, HttpContext context, CheckoutService service, CancellationToken ct) =>
    service.Identify(ActorOf(context), body.Kind, body.Value, ct)).WithTags("Caixa").ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
staff.MapDelete("/checkout/sessions/{token:guid}", async (Guid token, HttpContext context, CheckoutService service, CancellationToken ct) =>
{
    await service.Release(ActorOf(context), token, ct);
    return TypedResults.NoContent();
}).WithTags("Caixa");
staff.MapGet("/checkout/sessions/{token:guid}/spending", (Guid token, int? page, HttpContext context,
    CheckoutService service, CancellationToken ct) => service.Spending(ActorOf(context), token, page ?? 1, ct))
    .WithTags("Caixa").ProducesProblem(400).ProducesProblem(409);
staff.MapPost("/checkout/quote", (Cart body, HttpContext context, CheckoutService service, CancellationToken ct) =>
    service.Quote(ActorOf(context), body, ct)).WithTags("Caixa").ProducesProblem(400).ProducesProblem(409).ProducesProblem(422).ProducesProblem(503);
staff.MapPost("/orders", (Finalization body, [FromHeader(Name = "Idempotency-Key")] Guid key, HttpContext context,
    CheckoutService service, CancellationToken ct) => key == Guid.Empty
    ? throw new BusinessError(400, "missing_idempotency_key", "Informe o cabeçalho Idempotency-Key com um UUID.")
    : service.Confirm(ActorOf(context), body, key, ct)).WithTags("Caixa").ProducesProblem(400).ProducesProblem(409)
    .ProducesProblem(422).ProducesProblem(503);
staff.MapGet("/orders/{id:guid}", (Guid id, HttpContext context, CheckoutService service, CancellationToken ct) =>
    service.GetReceipt(ActorOf(context), id, ct)).WithTags("Caixa").ProducesProblem(404);

var management = staff.MapGroup("").RequireAuthorization("management").WithTags("Gestão");
management.MapGet("/reports/orders", (DateOnly? from, DateOnly? to, int? page, HttpContext context,
    ManagementService service, CancellationToken ct) => service.Report(ActorOf(context), from, to, page ?? 1, ct));
management.MapPost("/orders/{id:guid}/reverse", (Guid id, ReverseRequest body, HttpContext context,
    CheckoutService service, CancellationToken ct) => service.Reverse(ActorOf(context), id, body.Reason, body.RefundReference, ct))
    .ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);

var admin = staff.MapGroup("").RequireAuthorization("admin").WithTags("Administração");
admin.MapGet("/products", (HttpContext context, ManagementService service, CancellationToken ct) => service.Products(ActorOf(context), ct));
admin.MapPost("/products", (ProductRequest body, HttpContext context, ManagementService service, CancellationToken ct) =>
    service.SaveProduct(ActorOf(context), null, body, ct)).ProducesProblem(409).ProducesProblem(422);
admin.MapPut("/products/{id:guid}", (Guid id, ProductRequest body, HttpContext context, ManagementService service, CancellationToken ct) =>
    service.SaveProduct(ActorOf(context), id, body, ct)).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);
admin.MapPut("/meal-rule", (RuleRequest body, HttpContext context, ManagementService service, CancellationToken ct) =>
    service.SaveRule(ActorOf(context), body, ct)).ProducesProblem(409).ProducesProblem(422);
admin.MapGet("/integration/events", (HttpContext context, ManagementService service, CancellationToken ct) =>
    service.Integrations(ActorOf(context), ct));
admin.MapPost("/integration/events/{id:guid}/retry", async (Guid id, HttpContext context, ManagementService service, CancellationToken ct) =>
{
    await service.Retry(ActorOf(context), id, ct);
    return TypedResults.NoContent();
}).ProducesProblem(409);

app.Run();

static Actor ActorOf(HttpContext context) => (Actor)context.Items[typeof(Actor)]!;

internal sealed class PostgreSqlDate : SqlMapper.TypeHandler<DateOnly>
{
    public override DateOnly Parse(object value) => value is DateOnly date ? date : DateOnly.FromDateTime((DateTime)value);
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value;
    }
}

internal sealed class PostgreSqlTime : SqlMapper.TypeHandler<TimeOnly>
{
    public override TimeOnly Parse(object value) => value is TimeOnly time ? time : TimeOnly.FromTimeSpan((TimeSpan)value);
    public override void SetValue(IDbDataParameter parameter, TimeOnly value)
    {
        parameter.DbType = DbType.Time;
        parameter.Value = value;
    }
}

public partial class Program { }
