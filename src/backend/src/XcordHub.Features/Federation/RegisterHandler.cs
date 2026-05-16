using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Federation;

public sealed record RegisterCommand(string BootstrapToken, string? ClientIp);

public sealed record RegisterResponse(string InstanceOAuthToken, string InstanceId, string Domain);

public sealed class RegisterHandler(
    HubDbContext dbContext,
    SnowflakeIdGenerator snowflakeGenerator,
    ILogger<RegisterHandler> logger)
    : IRequestHandler<RegisterCommand, Result<RegisterResponse>>, IValidatable<RegisterCommand>
{
    public Error? Validate(RegisterCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.BootstrapToken))
            return Error.Validation("VALIDATION_FAILED", "Bootstrap token is required");

        return null;
    }

    public async Task<Result<RegisterResponse>> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        // Hash the provided bootstrap token
        var bootstrapTokenHash = TokenHelper.HashToken(request.BootstrapToken);

        // Find instance with matching bootstrap token
        var instance = await dbContext.ManagedInstances
            .Include(i => i.Infrastructure)
            .FirstOrDefaultAsync(
                i => i.Infrastructure != null && i.Infrastructure.BootstrapTokenHash == bootstrapTokenHash,
                cancellationToken);

        if (instance == null)
        {
            // Audit failed attempt: log only the source IP and a token-hash prefix so we can
            // correlate brute-force attempts without storing the rejected token itself.
            var prefix = bootstrapTokenHash.Length >= 8 ? bootstrapTokenHash[..8] : bootstrapTokenHash;
            logger.LogWarning(
                "Federation register rejected: invalid bootstrap token from {ClientIp} (hash prefix {HashPrefix})",
                request.ClientIp ?? "unknown",
                prefix);
            return Error.Validation("INVALID_BOOTSTRAP_TOKEN", "Invalid bootstrap token");
        }

        // Verify instance is in Running status
        if (instance.Status != InstanceStatus.Running)
        {
            logger.LogWarning(
                "Federation register rejected: instance {InstanceId} status {Status} (from {ClientIp})",
                instance.Id, instance.Status, request.ClientIp ?? "unknown");
            return Error.Validation("INSTANCE_NOT_RUNNING", "Instance is not in running state");
        }

        // Check if a federation token already exists for this instance
        var existingToken = await dbContext.FederationTokens
            .FirstOrDefaultAsync(
                t => t.ManagedInstanceId == instance.Id && t.RevokedAt == null,
                cancellationToken);

        if (existingToken != null)
        {
            return Error.Conflict("TOKEN_ALREADY_EXISTS", "Instance already has an active federation token");
        }

        // Generate new OAuth token
        var oauthToken = TokenHelper.GenerateToken();
        var oauthTokenHash = TokenHelper.HashToken(oauthToken);
        var now = DateTimeOffset.UtcNow;

        var federationToken = new FederationToken
        {
            Id = snowflakeGenerator.NextId(),
            ManagedInstanceId = instance.Id,
            TokenHash = oauthTokenHash,
            CreatedAt = now
        };

        dbContext.FederationTokens.Add(federationToken);

        // Clear the bootstrap token hash so it can't be reused
        if (instance.Infrastructure != null)
        {
            instance.Infrastructure.BootstrapTokenHash = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RegisterResponse(oauthToken, instance.Id.ToString(), instance.Domain);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/federation/register", async (
            RegisterRequest body,
            RegisterHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
            var command = new RegisterCommand(body.BootstrapToken, clientIp);
            return await handler.ExecuteAsync(command, ct).ConfigureAwait(false);
        })
        .AllowAnonymous()
        .RequireRateLimiting("BootstrapToken")
        .Produces<RegisterResponse>(200)
        .WithName("FederationRegister")
        .WithTags("Federation");
    }
}

/// <summary>
/// Public-facing body for /api/v1/federation/register. The internal RegisterCommand
/// additionally carries the client IP (resolved from HttpContext) for audit logging
/// failed attempts; the IP is never accepted from the request body.
/// </summary>
public sealed record RegisterRequest(string BootstrapToken);
