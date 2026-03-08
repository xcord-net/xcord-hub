using System.Reflection;

namespace XcordHub.Api;

public static class HealthEndpoint
{
    private static readonly string Version = typeof(HealthEndpoint).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public static void MapHealthEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", version = Version, timestamp = DateTimeOffset.UtcNow }))
           .WithTags("Health")
           .AllowAnonymous();
    }
}
