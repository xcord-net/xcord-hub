using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Auth;

public sealed record UpdateProfileRequest(
    string? DisplayName,
    string? Email
);

public sealed record UpdateProfileCommand(
    long UserId,
    string? DisplayName,
    string? Email
);

public sealed record UpdateProfileResponse(
    string DisplayName,
    string Email
);

public sealed class UpdateProfileHandler(
    HubDbContext dbContext,
    IEncryptionService encryptionService)
    : IRequestHandler<UpdateProfileCommand, Result<UpdateProfileResponse>>, IValidatable<UpdateProfileCommand>
{
    public Error? Validate(UpdateProfileCommand request)
    {
        if (request.DisplayName is not null && string.IsNullOrWhiteSpace(request.DisplayName))
            return Error.Validation("VALIDATION_FAILED", "Display name cannot be empty");

        if (request.DisplayName is not null && request.DisplayName.Length > 100)
            return Error.Validation("VALIDATION_FAILED", "Display name must be 100 characters or fewer");

        if (request.Email is not null && string.IsNullOrWhiteSpace(request.Email))
            return Error.Validation("VALIDATION_FAILED", "Email cannot be empty");

        if (request.Email is not null && request.Email.Length > 255)
            return Error.Validation("VALIDATION_FAILED", "Email must be 255 characters or fewer");

        if (request.Email is not null && !ValidationHelpers.IsValidEmail(request.Email))
            return Error.Validation("VALIDATION_FAILED", "Invalid email address");

        return null;
    }

    public async Task<Result<UpdateProfileResponse>> Handle(
        UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        var user = await dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.DeletedAt == null, cancellationToken);

        if (user == null)
            return Error.NotFound("USER_NOT_FOUND", "User not found");

        if (request.DisplayName is not null)
        {
            user.DisplayName = request.DisplayName;
        }

        if (request.Email is not null)
        {
            var normalizedEmail = request.Email.ToLowerInvariant();
            var emailHash = encryptionService.ComputeHmac(normalizedEmail);

            var emailExists = await dbContext.HubUsers
                .AnyAsync(u => u.Id != request.UserId && u.EmailHash == emailHash && u.DeletedAt == null, cancellationToken);

            if (emailExists)
                return Error.Conflict("EMAIL_TAKEN", "Email is already in use");

            user.Email = encryptionService.Encrypt(normalizedEmail);
            user.EmailHash = emailHash;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var decryptedEmail = encryptionService.Decrypt(user.Email);

        return new UpdateProfileResponse(
            DisplayName: user.DisplayName,
            Email: decryptedEmail
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPatch("/api/v1/auth/profile", async (
            [FromBody] UpdateProfileRequest request,
            ClaimsPrincipal user,
            UpdateProfileHandler handler,
            CancellationToken ct) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !long.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            var command = new UpdateProfileCommand(
                userId,
                request.DisplayName,
                request.Email
            );

            return await handler.ExecuteAsync(command, ct);
        })
        .RequireAuthorization(Policies.User)
        .Produces<UpdateProfileResponse>(200)
        .WithName("UpdateProfile")
        .WithTags("Auth");
    }
}
