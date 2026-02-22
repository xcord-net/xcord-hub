using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Discovery;

public sealed record ListInstancesQuery(
    string? Search,
    string? SortBy,
    int Page,
    int PageSize
);

public sealed record ListInstancesResponse(
    List<InstancePreview> Instances,
    int TotalCount,
    int Page,
    int PageSize
);

public sealed record InstancePreview(
    string Id,
    string Name,
    string Description,
    string IconUrl,
    string Domain,
    int MemberCount,
    int OnlineCount
);

public sealed class ListInstancesHandler(HubDbContext dbContext)
    : IRequestHandler<ListInstancesQuery, Result<ListInstancesResponse>>
{
    public async Task<Result<ListInstancesResponse>> Handle(ListInstancesQuery request, CancellationToken cancellationToken)
    {
        // Start with base query - only Running instances
        var query = dbContext.ManagedInstances
            .Where(i => i.Status == InstanceStatus.Running);

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var searchLower = request.Search.ToLower();
            query = query.Where(i =>
                i.DisplayName.ToLower().Contains(searchLower) ||
                (i.Description != null && i.Description.ToLower().Contains(searchLower)));
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync(cancellationToken);

        // Apply sorting
        query = request.SortBy?.ToLower() switch
        {
            "members" => query.OrderByDescending(i => i.MemberCount),
            "online" => query.OrderByDescending(i => i.OnlineCount),
            "name" => query.OrderBy(i => i.DisplayName),
            _ => query.OrderByDescending(i => i.CreatedAt) // default: newest first
        };

        // Apply pagination
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var rawInstances = await query
            .Skip(skip)
            .Take(pageSize)
            .Select(i => new
            {
                i.Id,
                i.DisplayName,
                Description = i.Description ?? string.Empty,
                IconUrl = i.IconUrl ?? string.Empty,
                i.Domain,
                i.MemberCount,
                i.OnlineCount
            })
            .ToListAsync(cancellationToken);

        var instances = rawInstances
            .Select(i => new InstancePreview(
                i.Id.ToString(),
                i.DisplayName,
                i.Description,
                i.IconUrl,
                i.Domain,
                i.MemberCount,
                i.OnlineCount
            ))
            .ToList();

        return new ListInstancesResponse(instances, totalCount, page, pageSize);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/discover/instances", async (
            string? search,
            string? sortBy,
            int page,
            int pageSize,
            ListInstancesHandler handler,
            CancellationToken ct) =>
        {
            var query = new ListInstancesQuery(search, sortBy, page, pageSize);
            return await handler.ExecuteAsync(query, ct);
        })
        .AllowAnonymous()
        .Produces<ListInstancesResponse>(200)
        .WithName("DiscoveryListInstances")
        .WithTags("Discovery");
    }
}
