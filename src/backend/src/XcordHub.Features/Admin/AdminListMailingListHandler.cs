using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Admin;

public sealed record AdminListMailingListQuery(
    int Page = 1,
    int PageSize = 25,
    string? Tier = null
);

public sealed record AdminListMailingListResponse(
    List<MailingListItem> Entries,
    int Total,
    int Page,
    int PageSize
);

public sealed record MailingListItem(
    long Id,
    string Email,
    string Tier,
    DateTimeOffset CreatedAt
);

public sealed class AdminListMailingListHandler(HubDbContext dbContext)
    : IRequestHandler<AdminListMailingListQuery, Result<AdminListMailingListResponse>>
{
    public async Task<Result<AdminListMailingListResponse>> Handle(AdminListMailingListQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.MailingListEntries.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Tier))
        {
            query = query.Where(e => e.Tier == request.Tier);
        }

        var total = await query.CountAsync(cancellationToken);

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var entries = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .Select(e => new MailingListItem(e.Id, e.Email, e.Tier, e.CreatedAt))
            .ToListAsync(cancellationToken);

        return new AdminListMailingListResponse(entries, total, page, pageSize);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/admin/mailing-list", async (
            int page,
            int pageSize,
            string? tier,
            AdminListMailingListHandler handler,
            CancellationToken ct) =>
        {
            var effectivePage = page > 0 ? page : 1;
            var effectivePageSize = pageSize > 0 ? pageSize : 25;
            return await handler.ExecuteAsync(new AdminListMailingListQuery(effectivePage, effectivePageSize, tier), ct);
        })
        .RequireAuthorization(Policies.Admin)
        .WithName("AdminListMailingList")
        .WithTags("Admin");
    }
}
