using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Admin;

public sealed record RotateDataKeyCommand;

public sealed record RotateDataKeyResponse(int NewVersion);

/// <summary>
/// Admin-only endpoint to rotate the hub's AES-GCM Data Encryption Key (DEK).
/// Generates a fresh DEK, wraps it under the configured KEK, persists it as a
/// new active row in encrypted_data_keys, and switches the in-memory key holder
/// so subsequent encryptions use the new version. Existing ciphertext continues
/// to decrypt under its original version transparently.
/// </summary>
public sealed class RotateDataKeyHandler(
    IKeyRotationService rotationService,
    ILogger<RotateDataKeyHandler> logger)
    : IRequestHandler<RotateDataKeyCommand, Result<RotateDataKeyResponse>>
{
    public async Task<Result<RotateDataKeyResponse>> Handle(
        RotateDataKeyCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var newVersion = await rotationService.RotateDataKeyAsync(cancellationToken);
            logger.LogInformation("Hub DEK rotation completed; new active version is {Version}", newVersion);
            return new RotateDataKeyResponse(newVersion);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Hub DEK rotation rejected: {Reason}", ex.Message);
            return Error.Failure("KEY_ROTATION_UNAVAILABLE", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hub DEK rotation failed");
            return Error.Failure("KEY_ROTATION_FAILED", "Failed to rotate the data encryption key.");
        }
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/admin/keys/rotate", async (
            RotateDataKeyHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new RotateDataKeyCommand(), ct,
                response => Results.Ok(response));
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<RotateDataKeyResponse>(200)
        .WithName("RotateDataKey")
        .WithTags("Admin", "Security");
    }
}
