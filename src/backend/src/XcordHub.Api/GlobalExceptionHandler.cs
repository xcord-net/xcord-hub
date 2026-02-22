using Microsoft.AspNetCore.Diagnostics;
using System.Text.Json;

namespace XcordHub.Api;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);
        httpContext.Response.StatusCode = 500;
        httpContext.Response.ContentType = "application/json";
        var response = new { error = "An unexpected error occurred" };
        await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);
        return true;
    }
}
