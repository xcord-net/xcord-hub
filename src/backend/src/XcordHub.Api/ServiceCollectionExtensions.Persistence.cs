using Serilog;
using StackExchange.Redis;
using XcordHub.Api.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Api;

public static partial class ServiceCollectionExtensions
{
    private static void AddEncryption(IServiceCollection services, IConfiguration config, IWebHostEnvironment environment)
    {
        services.AddSingleton<IKekProvider, FileKekProvider>();

        // Resolve KEK inline (before DI container is built)
        byte[]? hubKek = null;
        {
            var kekFile = config.GetSection("Encryption:KekFile").Value ?? "/run/secrets/xcord-kek";
            var kekBase64 = config.GetSection("Encryption:Kek").Value;
            if (File.Exists(kekFile))
            {
                hubKek = Convert.FromBase64String(File.ReadAllText(kekFile).Trim());
                Log.Information("Hub KEK loaded from file {KekFile}", kekFile);
            }
            else if (!string.IsNullOrEmpty(kekBase64))
            {
                hubKek = Convert.FromBase64String(kekBase64);
                Log.Information("Hub KEK loaded from configuration");
            }
        }
        var encryptionKeyRaw = config.GetSection("Encryption:Key").Value;
        var wrappedKeyRaw = config.GetSection("Encryption:WrappedKey").Value;

        string resolvedEncryptionKey;
        if (hubKek != null)
        {
            if (!string.IsNullOrEmpty(wrappedKeyRaw))
            {
                var wrappedBytes = Convert.FromBase64String(wrappedKeyRaw);
                var dekBytes = KeyWrappingService.UnwrapDek(wrappedBytes, hubKek);
                resolvedEncryptionKey = Convert.ToBase64String(dekBytes);
                Log.Information("Hub encryption key unwrapped using KEK");
            }
            else if (!string.IsNullOrEmpty(encryptionKeyRaw))
            {
                resolvedEncryptionKey = encryptionKeyRaw;
                Log.Warning("Hub has KEK configured but encryption key is plaintext - wrap the key for production use");
            }
            else
            {
                throw new InvalidOperationException("KEK is configured but no encryption key (Key or WrappedKey) is provided");
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(wrappedKeyRaw))
            {
                throw new InvalidOperationException(
                    "Wrapped encryption key configured but no KEK is available. " +
                    "Provide the KEK via /run/secrets/xcord-kek or Encryption:Kek config.");
            }

            // In Production, refuse to start without KEK unless explicitly opted out
            if (environment.IsProduction())
            {
                var allowPlaintext = config.GetValue<bool>("Encryption:AllowPlaintextDek", false);
                if (!allowPlaintext)
                {
                    throw new InvalidOperationException(
                        "Production environment requires a KEK (Key Encryption Key) for envelope encryption. " +
                        "Provide a KEK via /run/secrets/xcord-kek or Encryption:Kek config. " +
                        "To accept the risk of plaintext DEK storage, set Encryption:AllowPlaintextDek=true.");
                }
            }

            resolvedEncryptionKey = encryptionKeyRaw
                ?? throw new InvalidOperationException("Encryption key not configured");
            Log.Warning("Hub encryption key loaded WITHOUT envelope encryption - configure a KEK for production use");
        }
        // Register the key holder seeded with the bootstrap key as version 1.
        // BootstrapEncryptionKeyringAsync (called from Program.cs after MigrateAsync)
        // will reconcile this with the encrypted_data_keys table: either backfilling
        // version 1 from this seed, or replacing it with whatever the table already
        // contains (including any rotations that have happened since this process
        // last ran).
        var keyHolder = new EncryptionKeyHolder();
        keyHolder.SetKey(resolvedEncryptionKey);
        services.AddSingleton(keyHolder);
        services.AddSingleton<IEncryptionService>(sp =>
            new AesEncryptionService(sp.GetRequiredService<EncryptionKeyHolder>()));
        services.AddScoped<IKeyRotationService, KeyRotationService>();
        services.AddSingleton<ICursorService, CursorService>();
    }

    private static void AddRedis(IServiceCollection services, IConfiguration config)
    {
        var redisConnectionString = config.GetSection("Redis:ConnectionString").Value
            ?? throw new InvalidOperationException("Redis connection string not configured");

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var configurationOptions = ConfigurationOptions.Parse(redisConnectionString);
            configurationOptions.AbortOnConnectFail = false;
            configurationOptions.ConnectTimeout = 5000;
            configurationOptions.SyncTimeout = 1000;
            configurationOptions.ConnectRetry = 3;
            return ConnectionMultiplexer.Connect(configurationOptions);
        });
    }
}
