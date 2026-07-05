using CartService.Api;
using CartService.Api.Endpoints;
using CartService.Api.Middlewares;
using CartService.Domain.Services;
using CartService.Infrastructure;
using CartService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSwag;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ─── Serilog ───────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

// preserveStaticLogger: each host instance gets its own logger instead of
// freezing the static bootstrap logger — required when multiple app instances
// run in one process (e.g. WebApplicationFactory in integration tests).
builder.Host.UseSerilog((ctx, config) =>
    config.ReadFrom.Configuration(ctx.Configuration),
    preserveStaticLogger: true);

// ─── Services ──────────────────────────────────────────────
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<CartOperations>();

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CartDbContext>();

// Exception handler (Problem Details)
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddProblemDetails();

// OpenAPI / Swagger (ApiExplorer is required by NSwag for minimal APIs)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApiDocument(options =>
{
    options.Title = "Cart Service API";
    options.Version = "v1";
});

// ─── Auth (feature-flagged) ───────────────────────────────
var authEnabled = builder.Configuration.GetValue<bool>("Auth:Enabled", false);

if (authEnabled)
{
    var authority = builder.Configuration.GetValue<string>("Auth:Authority")
        ?? "http://localhost:8081/realms/retail";
    var audience = builder.Configuration.GetValue<string>("Auth:Audience")
        ?? "cart-service";

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = audience;
            options.RequireHttpsMetadata = false;
            // Keep original JWT claim names ("sub") instead of remapping to the
            // legacy ClaimTypes.NameIdentifier URI, so ownership can read "sub".
            options.MapInboundClaims = false;
        });

    builder.Services.AddAuthorization(options =>
    {
        // IdPs (e.g. Keycloak) issue "scope" as a single space-delimited claim
        // ("openid cart:read"), so an exact RequireClaim match would never pass.
        options.AddPolicy("CartRead", policy => policy.RequireAuthenticatedUser()
            .RequireAssertion(ctx => HasScope(ctx.User, "cart:read")));
        options.AddPolicy("CartWrite", policy => policy.RequireAuthenticatedUser()
            .RequireAssertion(ctx => HasScope(ctx.User, "cart:write")));
    });
}

static bool HasScope(System.Security.Claims.ClaimsPrincipal user, string scope) =>
    user.FindAll("scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Contains(scope);

// ─── App ───────────────────────────────────────────────────
var app = builder.Build();

// Middleware pipeline — exception handler first, correlation ID before
// request logging so every log line (incl. request logs) carries the ID.
app.UseExceptionHandler();
app.UseCorrelationId();
app.UseSerilogRequestLogging();

if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

// OpenAPI / Swagger UI
app.UseOpenApi();
app.UseSwaggerUi();

// Endpoints
app.MapHealthEndpoints();
app.MapCartEndpoints(requireAuth: authEnabled);

// Database initialization (migrations + seed)
await app.EnsureDatabaseAsync();

app.Run();
