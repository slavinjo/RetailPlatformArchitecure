using Microsoft.Extensions.Primitives;

namespace CartService.Api.Middlewares;

/// <summary>
/// Middleware that reads or generates a correlation ID for every request.
/// Supports W3C Trace Context (traceparent) and X-Correlation-ID headers.
/// </summary>
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        string correlationId = ExtractOrGenerateCorrelationId(context);
        context.Response.Headers.Append("X-Correlation-ID", correlationId);

        using (logger.BeginScope(new List<KeyValuePair<string, object>>
        {
            new("CorrelationId", correlationId),
            new("TraceId", correlationId)
        }))
        {
            await _next(context);
        }
    }

    private static string ExtractOrGenerateCorrelationId(HttpContext context)
    {
        // Try W3C traceparent first: "version-traceid-parentid-flags" (dash-separated)
        if (context.Request.Headers.TryGetValue("traceparent", out StringValues traceparent) && !string.IsNullOrEmpty(traceparent))
        {
            var parts = traceparent.ToString().Split('-');
            if (parts.Length == 4 && parts[1].Length == 32)
                return parts[1]; // trace-id from traceparent
        }

        // Try X-Correlation-ID
        if (context.Request.Headers.TryGetValue("X-Correlation-ID", out StringValues corrId) && !string.IsNullOrEmpty(corrId))
            return corrId.ToString()!;

        // Generate new
        return Guid.NewGuid().ToString("N");
    }
}

/// <summary>
/// Extension to register correlation ID middleware.
/// </summary>
public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }
}
