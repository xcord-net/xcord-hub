using Microsoft.AspNetCore.Http.Metadata;

namespace XcordHub.Api;

public sealed class HandlerLoggingFilter(Serilog.ILogger logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var name = ctx.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName ?? "Unknown";
        logger.Information("Handling {HandlerName}", name);
        try
        {
            var result = await next(ctx);
            logger.Information("Handled {HandlerName}", name);
            return result;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error handling {HandlerName}", name);
            throw;
        }
    }
}
