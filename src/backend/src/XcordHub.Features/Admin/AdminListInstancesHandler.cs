using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Admin;

public sealed record AdminListInstancesQuery(
    int Page = 1,
    int PageSize = 25,
    string? Status = null
);

public sealed record AdminListInstancesResponse(
    List<AdminInstanceListItem> Instances,
    int Total,
    int Page,
    int PageSize
);

public sealed record AdminInstanceListItem(
    long Id,
    string Subdomain,
    string DisplayName,
    string Status,
    string Tier,
    DateTimeOffset CreatedAt,
    string OwnerUsername
);

public sealed class AdminListInstancesHandler(HubDbContext dbContext)
    : IRequestHandler<AdminListInstancesQuery, Result<AdminListInstancesResponse>>
{
    public async Task<Result<AdminListInstancesResponse>> Handle(AdminListInstancesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.ManagedInstances
            .Include(i => i.Owner)
            .Include(i => i.Billing)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<InstanceStatus>(request.Status, true, out var status))
        {
            query = query.Where(i => i.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var rawInstances = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .Select(i => new
            {
                i.Id,
                i.Domain,
                i.DisplayName,
                i.Status,
                Tier = i.Billing != null ? i.Billing.Tier : (BillingTier?)null,
                i.CreatedAt,
                OwnerUsername = i.Owner.Username
            })
            .ToListAsync(cancellationToken);

        var instances = rawInstances.Select(i => new AdminInstanceListItem(
            i.Id,
            i.Domain.Contains('.') ? i.Domain[..i.Domain.IndexOf('.')] : i.Domain,
            i.DisplayName,
            i.Status.ToString(),
            i.Tier?.ToString() ?? "Free",
            i.CreatedAt,
            i.OwnerUsername
        )).ToList();

        return new AdminListInstancesResponse(instances, total, page, pageSize);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/admin/instances", async (
            int page,
            int pageSize,
            string? status,
            AdminListInstancesHandler handler,
            CancellationToken ct) =>
        {
            var effectivePage = page > 0 ? page : 1;
            var effectivePageSize = pageSize > 0 ? pageSize : 25;
            var query = new AdminListInstancesQuery(effectivePage, effectivePageSize, status);
            return await handler.ExecuteAsync(query, ct);
        })
        .RequireAuthorization(Policies.Admin)
        .WithName("AdminListInstances")
        .WithTags("Admin");
    }
}
