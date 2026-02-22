using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Federation;

public sealed record RegisterCommand(string BootstrapToken);

public sealed record RegisterResponse(string InstanceOAuthToken, long InstanceId, string Domain);

public sealed class RegisterHandler(HubDbContext dbContext, SnowflakeId snowflakeGenerator)
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
        var bootstrapTokenHash = HashToken(request.BootstrapToken);

        // Find instance with matching bootstrap token
        var instance = await dbContext.ManagedInstances
            .Include(i => i.Secrets)
            .FirstOrDefaultAsync(
                i => i.Secrets != null && i.Secrets.BootstrapTokenHash == bootstrapTokenHash,
                cancellationToken);

        if (instance == null)
        {
            return Error.Validation("INVALID_BOOTSTRAP_TOKEN", "Invalid bootstrap token");
        }

        // Verify instance is in Running status
        if (instance.Status != InstanceStatus.Running)
        {
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
        var oauthToken = GenerateOAuthToken();
        var oauthTokenHash = HashToken(oauthToken);
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
        if (instance.Secrets != null)
        {
            instance.Secrets.BootstrapTokenHash = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterResponse(oauthToken, instance.Id, instance.Domain);
    }

    private static string GenerateOAuthToken()
    {
        var randomBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hashBytes);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/federation/register", async (
            RegisterCommand request,
            RegisterHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(request, ct);
        })
        .AllowAnonymous()
        .WithName("FederationRegister")
        .WithTags("Federation");
    }
}
