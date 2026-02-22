using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Auth;

public sealed record GetMeQuery();

public sealed record GetMeResponse(
    string UserId,
    string Username,
    string DisplayName,
    string Email
);

public sealed class GetMeHandler(HubDbContext dbContext, ICurrentUserService currentUserService, IEncryptionService encryptionService)
    : IRequestHandler<GetMeQuery, Result<GetMeResponse>>
{
    public async Task<Result<GetMeResponse>> Handle(GetMeQuery request, CancellationToken cancellationToken)
    {
        var userIdResult = currentUserService.GetCurrentUserId();
        if (userIdResult.IsFailure) return userIdResult.Error!;
        var userId = userIdResult.Value;

        var user = await dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, cancellationToken);

        if (user == null)
            return Error.NotFound("USER_NOT_FOUND", "User not found");

        var email = encryptionService.Decrypt(user.Email);

        return new GetMeResponse(
            UserId: userId.ToString(),
            Username: user.Username,
            DisplayName: user.DisplayName,
            Email: email
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/auth/me", async (
            GetMeHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetMeQuery(), ct);
        })
        .RequireAuthorization(Policies.User)
        .WithName("GetMe")
        .WithTags("Auth");
    }
}
