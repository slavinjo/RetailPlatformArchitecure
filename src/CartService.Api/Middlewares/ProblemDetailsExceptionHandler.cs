using CartService.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CartService.Api.Middlewares;

/// <summary>
/// Global exception handler that maps domain exceptions to RFC 7807 Problem Details responses.
/// Implements IExceptionHandler for ASP.NET Core exception handling.
/// </summary>
public class ProblemDetailsExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<ProblemDetailsExceptionHandler> _logger;

    public ProblemDetailsExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<ProblemDetailsExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        (var statusCode, var type, var title) = exception switch
        {
            DomainException d => MapDomainException(d),
            KeyNotFoundException => (StatusCodes.Status404NotFound,
                "https://cartservice/errors/not_found", "Resource not found"),
            _ => (StatusCodes.Status500InternalServerError,
                "https://cartservice/errors/internal", "Internal server error")
        };

        // Domain rule violations are expected request outcomes, not server faults
        if (exception is DomainException)
            _logger.LogWarning("Domain rule violation while processing {Path}: {Message}", httpContext.Request.Path, exception.Message);
        else
            _logger.LogError(exception, "Unhandled exception while processing {Path}", httpContext.Request.Path);

        httpContext.Response.StatusCode = statusCode;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Type = type,
                Title = title,
                Detail = exception.Message,
                Instance = httpContext.Request.Path.ToString()
            }
        });
    }

    private static (int statusCode, string type, string title) MapDomainException(DomainException ex)
    {
        return ex.ErrorCode switch
        {
            "cart_not_found" => (StatusCodes.Status404NotFound,
                "https://cartservice/errors/cart_not_found", "Cart not found"),
            "cart_item_not_found" => (StatusCodes.Status404NotFound,
                "https://cartservice/errors/cart_item_not_found", "Cart item not found"),
            "product_not_found" => (StatusCodes.Status422UnprocessableEntity,
                "https://cartservice/errors/product_not_found", "Product not found"),
            "product_unavailable" => (StatusCodes.Status422UnprocessableEntity,
                "https://cartservice/errors/product_unavailable", "Product is not available"),
            "invalid_quantity" => (StatusCodes.Status422UnprocessableEntity,
                "https://cartservice/errors/invalid_quantity", "Invalid quantity"),
            "concurrency_conflict" => (StatusCodes.Status409Conflict,
                "https://cartservice/errors/concurrency_conflict", "Concurrency conflict"),
            "cart_forbidden" => (StatusCodes.Status403Forbidden,
                "https://cartservice/errors/cart_forbidden", "Forbidden"),
            _ => (StatusCodes.Status422UnprocessableEntity,
                "https://cartservice/errors/domain_error", "Domain rule violation")
        };
    }
}
