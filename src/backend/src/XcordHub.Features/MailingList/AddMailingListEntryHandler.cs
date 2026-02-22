using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.MailingList;

public sealed record AddMailingListEntryRequest(string Email, string Tier);

public sealed record AddMailingListEntryResponse(string Message);

public sealed class AddMailingListEntryHandler(HubDbContext dbContext, SnowflakeId snowflakeGenerator)
    : IRequestHandler<AddMailingListEntryRequest, Result<AddMailingListEntryResponse>>, IValidatable<AddMailingListEntryRequest>
{
    private static readonly HashSet<string> ValidTiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Basic", "Pro"
    };

    public Error? Validate(AddMailingListEntryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return Error.Validation("VALIDATION_FAILED", "Email is required.");

        if (request.Email.Length > 255)
            return Error.Validation("VALIDATION_FAILED", "Email must not exceed 255 characters.");

        if (!Regex.IsMatch(request.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return Error.Validation("VALIDATION_FAILED", "Invalid email format.");

        if (string.IsNullOrWhiteSpace(request.Tier))
            return Error.Validation("VALIDATION_FAILED", "Tier is required.");

        if (!ValidTiers.Contains(request.Tier))
            return Error.Validation("VALIDATION_FAILED", "Invalid tier. Must be one of: Basic, Pro.");

        return null;
    }

    public async Task<Result<AddMailingListEntryResponse>> Handle(AddMailingListEntryRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var exists = await dbContext.MailingListEntries
            .AnyAsync(e => e.Email == normalizedEmail && e.Tier == request.Tier, cancellationToken);

        if (exists)
        {
            return new AddMailingListEntryResponse("You're already on the list. We'll notify you when this plan is available.");
        }

        var entry = new MailingListEntry
        {
            Id = snowflakeGenerator.NextId(),
            Email = normalizedEmail,
            Tier = request.Tier,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.MailingListEntries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AddMailingListEntryResponse("You've been added to the mailing list. We'll notify you when this plan is available.");
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/mailing-list", async (
            AddMailingListEntryRequest request,
            AddMailingListEntryHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(request, ct);
        })
        .AllowAnonymous()
        .WithName("AddMailingListEntry")
        .WithTags("MailingList");
    }
}
