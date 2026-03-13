using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Auth;

public sealed record SetupStatusResponse(bool NeedsSetup);

public sealed class SetupStatusHandler : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/setup/status", async (HubDbContext dbContext, CancellationToken ct) =>
        {
            var hasUsers = await dbContext.HubUsers.AnyAsync(ct);
            return Results.Ok(new SetupStatusResponse(!hasUsers));
        })
        .AllowAnonymous()
        .Produces<SetupStatusResponse>(200)
        .WithName("SetupStatus")
        .WithTags("Setup");
    }
}
