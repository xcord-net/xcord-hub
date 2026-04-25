using Microsoft.EntityFrameworkCore;
using Serilog;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Reconciles the in-memory <see cref="EncryptionKeyHolder"/> with the persisted
/// <see cref="EncryptedDataKey"/> table. Called once at hub startup, after
/// <c>MigrateAsync</c>.
///
/// Behaviour:
/// - If the table has rows, all wrapped DEKs are unwrapped under the KEK and
///   loaded into the holder. The active version in the table becomes the
///   holder's active version. The DI-time seed is treated as transitional and
///   overwritten only for versions that the table also contains, so the
///   AES-key cache inside <see cref="AesEncryptionService"/> stays consistent.
/// - If the table is empty AND a KEK is configured AND the seed is a 32-byte
///   base64 DEK, the seed is wrapped and persisted as version 1.
/// - If the table is empty AND no KEK is configured, the holder is left alone
///   (development / no-envelope-encryption fallback). Rotation will refuse to
///   run in this state.
/// </summary>
public static class EncryptionKeyringBootstrap
{
    public static async Task ReconcileAsync(
        HubDbContext db,
        IKekProvider kekProvider,
        EncryptionKeyHolder keyHolder,
        CancellationToken cancellationToken = default)
    {
        var kek = kekProvider.GetKek();

        var existing = await db.EncryptedDataKeys
            .OrderBy(k => k.Version)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            if (kek == null)
            {
                throw new InvalidOperationException(
                    "Database contains wrapped DEKs in encrypted_data_keys but no KEK is configured. " +
                    "Provide the KEK via /run/secrets/xcord-kek or Encryption:Kek config.");
            }

            foreach (var row in existing)
            {
                var dek = KeyWrappingService.UnwrapDek(row.WrappedKey, kek);
                keyHolder.AddKey(
                    version: (byte)row.Version,
                    keyMaterial: Convert.ToBase64String(dek),
                    isActive: row.IsActive);
            }

            Log.Information(
                "Loaded {Count} hub encryption key version(s) from encrypted_data_keys; active version is {Active}",
                existing.Count, keyHolder.ActiveVersion);
            return;
        }

        if (kek == null)
        {
            Log.Warning(
                "No KEK configured; hub encryption keyring will run with the in-memory seed only. " +
                "Configure a KEK and restart to enable rotation.");
            return;
        }

        // Wrap the DI-time seed and persist it as version 1 so future boots take
        // the load path above. The seed must be 32-byte base64 (the standard hub
        // DEK format); if it isn't we cannot persist it without breaking the
        // wrapping invariants, so we skip and log.
        var seedDekBase64 = keyHolder.GetKey(keyHolder.ActiveVersion);
        byte[] seedDekBytes;
        try
        {
            seedDekBytes = Convert.FromBase64String(seedDekBase64);
        }
        catch (FormatException)
        {
            Log.Warning(
                "Hub encryption seed is not a base64 string; encrypted_data_keys cannot be initialised. " +
                "Provide a 32-byte base64 DEK via Encryption:Key or Encryption:WrappedKey to enable rotation.");
            return;
        }

        if (seedDekBytes.Length != 32)
        {
            Log.Warning(
                "Hub encryption seed is {Length} bytes (expected 32); encrypted_data_keys cannot be initialised.",
                seedDekBytes.Length);
            return;
        }

        var wrapped = KeyWrappingService.WrapDek(seedDekBytes, kek);
        db.EncryptedDataKeys.Add(new EncryptedDataKey
        {
            Version = 1,
            WrappedKey = wrapped,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);

        Log.Information("Hub encryption keyring initialized at version 1 (envelope-encrypted)");
    }
}
