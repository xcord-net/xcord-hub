using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Billing;

public sealed record GetInvoicesQuery(int Limit = 25);

public sealed record InvoiceSummary(
    string Id,
    string Description,
    long AmountCents,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt,
    string? PdfUrl
);

public sealed record GetInvoicesResponse(List<InvoiceSummary> Invoices);

public sealed class GetInvoicesHandler(HubDbContext dbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetInvoicesQuery, Result<GetInvoicesResponse>>
{
    public async Task<Result<GetInvoicesResponse>> Handle(
        GetInvoicesQuery request, CancellationToken cancellationToken)
    {
        var userIdResult = currentUserService.GetCurrentUserId();
        if (userIdResult.IsFailure) return userIdResult.Error!;
        var userId = userIdResult.Value;

        var user = await dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, cancellationToken);

        if (user == null)
            return Error.NotFound("USER_NOT_FOUND", "User not found");

        // TODO: Fetch invoices from Stripe API using user.StripeCustomerId when billing is wired up.
        var invoices = new List<InvoiceSummary>();

        return new GetInvoicesResponse(invoices);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/hub/billing/invoices", async (
            GetInvoicesHandler handler,
            int? limit,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetInvoicesQuery(limit ?? 25), ct);
        })
        .RequireAuthorization(Policies.User)
        .WithName("GetInvoices")
        .WithTags("Billing");
    }
}
