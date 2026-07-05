using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CartService.Api.Endpoints;

/// <summary>
/// Health check endpoints.
/// /health/live — liveness probe (always OK if app is running)
/// /health/ready — readiness probe (checks DB connectivity)
/// </summary>
public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness — just check if the process is alive
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }))
            .WithName("HealthLive");

        // Readiness — use the built-in health checks infrastructure
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            }
        })
            .WithName("HealthReady");
    }
}
