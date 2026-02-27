using System.Security.Claims;
using BCrypt.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Auth;

public sealed record DeleteAccountRequest(string Password);

public sealed record DeleteAccountCommand(long UserId, string Password);

public sealed class DeleteAccountHandler(
    HubDbContext dbContext,
    IDockerService dockerService,
    ILogger<DeleteAccountHandler> logger)
    : IRequestHandler<DeleteAccountCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DeleteAccountCommand request, CancellationToken cancellationToken)
    {
        var user = await dbContext.HubUsers
            .Include(u => u.ManagedInstances)
                .ThenInclude(i => i.Infrastructure)
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.DeletedAt == null, cancellationToken);

        if (user == null)
        {
            return Error.NotFound("USER_NOT_FOUND", "User not found");
        }

        // Verify password before allowing deletion — offloaded to thread pool to avoid starvation
        if (!await Task.Run(() => BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash)))
        {
            return Error.Validation("INVALID_PASSWORD", "Password is incorrect");
        }

        // Admins cannot delete their own accounts to prevent lockout
        if (user.IsAdmin)
        {
            return Error.Forbidden("ADMIN_ACCOUNT", "Administrator accounts cannot be deleted");
        }

        var now = DateTimeOffset.UtcNow;

        // Suspend or destroy all active instances owned by the user
        var activeInstances = user.ManagedInstances
            .Where(i => i.DeletedAt == null && i.Status != InstanceStatus.Destroyed)
            .ToList();

        foreach (var instance in activeInstances)
        {
            try
            {
                logger.LogInformation(
                    "Suspending instance {InstanceId} ({Domain}) for deleted account {UserId}",
                    instance.Id, instance.Domain, user.Id);

                await TryStopInstanceAsync(instance, cancellationToken);

                instance.Status = InstanceStatus.Suspended;
                instance.DeletedAt = now;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to stop instance {InstanceId} ({Domain}) during account deletion",
                    instance.Id, instance.Domain);

                // Mark as destroyed even if stop failed — account deletion takes priority
                instance.Status = InstanceStatus.Destroyed;
                instance.DeletedAt = now;
            }
        }

        // Revoke all refresh tokens (invalidate all sessions)
        dbContext.RefreshTokens.RemoveRange(user.RefreshTokens);

        // Soft-delete the user account
        user.DeletedAt = now;
        user.IsDisabled = true;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Account {UserId} ({Username}) deleted", user.Id, user.Username);

        return true;
    }

    private async Task TryStopInstanceAsync(ManagedInstance instance, CancellationToken cancellationToken)
    {
        var infrastructure = instance.Infrastructure;
        if (infrastructure == null)
        {
            return;
        }

        // Stop the container if running
        if (!string.IsNullOrWhiteSpace(infrastructure.DockerContainerId) &&
            instance.Status == InstanceStatus.Running)
        {
            try
            {
                await dockerService.StopContainerAsync(infrastructure.DockerContainerId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to stop container {ContainerId}", infrastructure.DockerContainerId);
            }
        }
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapDelete("/api/v1/users/@me", async (
            [FromBody] DeleteAccountRequest request,
            ClaimsPrincipal user,
            DeleteAccountHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !long.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            var command = new DeleteAccountCommand(userId, request.Password);
            var result = await handler.Handle(command, ct);

            return result.Match(
                _ =>
                {
                    AuthCookieHelper.DeleteRefreshTokenCookie(httpContext);
                    return Results.Ok(new SuccessResponse(true));
                },
                error => Results.Problem(statusCode: error.StatusCode, title: error.Code, detail: error.Message));
        })
        .RequireAuthorization(Policies.User)
        .Produces<SuccessResponse>(200)
        .WithName("DeleteAccount")
        .WithTags("Auth");
    }
}
