using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Upgrades;

public sealed record PublishVersionRequest(
    string Version,
    string Image,
    string? ReleaseNotes = null,
    long PublishedBy = 0
);

public sealed record PublishVersionResponse(
    string Id,
    string Version,
    string Image,
    string? ReleaseNotes,
    DateTimeOffset PublishedAt
);

public sealed class PublishVersionHandler(HubDbContext dbContext, SnowflakeId snowflakeGenerator)
    : IRequestHandler<PublishVersionRequest, Result<PublishVersionResponse>>,
      IValidatable<PublishVersionRequest>
{
    public Error? Validate(PublishVersionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Version))
            return Error.Validation("VALIDATION_FAILED", "Version is required");

        if (string.IsNullOrWhiteSpace(request.Image))
            return Error.Validation("VALIDATION_FAILED", "Image is required");

        return null;
    }

    public async Task<Result<PublishVersionResponse>> Handle(
        PublishVersionRequest request, CancellationToken cancellationToken)
    {
        var exists = await dbContext.AvailableVersions
            .AnyAsync(v => v.Version == request.Version && v.DeletedAt == null, cancellationToken);

        if (exists)
            return Error.Conflict("VERSION_EXISTS", $"Version '{request.Version}' already exists");

        var now = DateTimeOffset.UtcNow;
        var version = new AvailableVersion
        {
            Id = snowflakeGenerator.NextId(),
            Version = request.Version,
            Image = request.Image,
            ReleaseNotes = request.ReleaseNotes,
            PublishedAt = now,
            PublishedBy = request.PublishedBy
        };

        dbContext.AvailableVersions.Add(version);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PublishVersionResponse(
            version.Id.ToString(),
            version.Version,
            version.Image,
            version.ReleaseNotes,
            version.PublishedAt
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/admin/versions", async (
            PublishVersionRequest request,
            PublishVersionHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = long.Parse(httpContext.User.FindFirst("sub")!.Value);
            return await handler.ExecuteAsync(request with { PublishedBy = userId }, ct,
                success => Results.Created($"/api/v1/admin/versions/{success.Id}", success));
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<PublishVersionResponse>(201)
        .WithName("PublishVersion")
        .WithTags("Admin", "Upgrades");
    }
}
