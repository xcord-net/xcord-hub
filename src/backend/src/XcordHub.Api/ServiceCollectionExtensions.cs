using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BCrypt.Net;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using XcordHub.Api.Auth;
using Minio;
using Serilog;
using StackExchange.Redis;
using XcordHub.Api.Options;
using XcordHub.Entities;
using XcordHub.Features;
using XcordHub.Features.Auth;
using XcordHub.Features.Instances;
using XcordHub.Features.Monitoring;
using XcordHub.Features.Destruction;
using XcordHub.Features.Provisioning;
using XcordHub.Features.Backups;
using XcordHub.Features.Upgrades;
using XcordHub.Features.Billing;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;
using CloudflareOptions = XcordHub.Infrastructure.Services.CloudflareOptions;
using DockerOptions = XcordHub.Infrastructure.Services.DockerOptions;
using LinodeOptions = XcordHub.Infrastructure.Services.LinodeOptions;
using Route53Options = XcordHub.Infrastructure.Services.Route53Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace XcordHub.Api;

public static class ServiceCollectionExtensions
{
    public static WebApplicationBuilder AddHubServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        // JSON serialization - explicit camelCase + converters
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.SerializerOptions.Converters.Add(new SnowflakeJsonConverter());
        });

        // Options
        AddOptions(services, config);

        // Database
        var connectionString = config.GetSection("Database:ConnectionString").Value
            ?? throw new InvalidOperationException("Database connection string not configured");
        services.AddDbContext<HubDbContext>(options => options.UseNpgsql(connectionString));

        // Snowflake ID generator
        services.AddSingleton(sp => new SnowflakeIdGenerator(1)); // workerId 1 for hub

        // Encryption
        AddEncryption(services, config, builder.Environment);

        // Captcha
        AddCaptcha(services, config);

        // Cold storage
        AddColdStorage(services, config);

        // Email
        services.AddScoped<IEmailService, SmtpEmailService>();

        // Stripe billing
        services.Configure<XcordHub.Infrastructure.Options.StripeOptions>(
            config.GetSection(XcordHub.Infrastructure.Options.StripeOptions.SectionName));
        services.AddScoped<IStripeService, XcordHub.Infrastructure.Services.StripeService>();
        services.AddScoped<XcordHub.Features.Billing.StripeWebhookHandler>();

        // JWT
        AddJwt(services, config);

        // HttpClient registrations
        AddHttpClients(services, config);

        // Provisioning
        AddProvisioning(services, config);

        // Upgrades
        services.AddSingleton<IUpgradeQueue, UpgradeQueue>();
        services.AddScoped<UpgradeOrchestrator>();

        // Background services
        services.AddHostedService<ProvisioningBackgroundService>();
        services.AddHostedService<HealthCheckMonitor>();
        services.AddHostedService<InstanceReconciler>();
        services.AddHostedService<UpgradeBackgroundService>();
        services.AddHostedService<MinimumVersionEnforcerService>();
        services.AddHostedService<ScheduledRolloutService>();
        services.AddScoped<BackupExecutor>();
        services.AddHostedService<BackupBackgroundService>();
        services.AddHostedService<UptimeTrackingService>();
        services.AddHostedService<ReportUsageToStripeService>();

        // Metrics
        services.AddSingleton<GatewayMetrics>();
        services.AddSingleton<ProvisioningMetrics>();

        // Redis
        AddRedis(services, config);

        // OpenTelemetry
        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(ResourceBuilder.CreateDefault()
                        .AddService("xcord-hub", serviceVersion: "1.0.0"))
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddConsoleExporter();
            });

        // HttpContext accessor (required by CurrentUserService)
        services.AddHttpContextAccessor();

        // Current user service
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Instance creation service
        services.AddScoped<InstanceCreationService>();

        // System config service (admin-toggleable runtime settings)
        services.AddScoped<ISystemConfigService, SystemConfigService>();

        // Request handlers
        services.AddRequestHandlers(typeof(FeaturesAssemblyMarker).Assembly);
        services.AddScoped<RefreshTokenHandler>();
        services.AddScoped<SetupHandler>();
        services.AddScoped<UserRegistrationService>();

        // Rate limiting
        AddRateLimiting(services, config);

        // CORS
        AddCors(services, config, builder.Environment);

        // Authentication & Authorization
        AddAuth(services, config);

        // OpenAPI
        services.AddOpenApi();

        // Exception handling
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        // Controllers
        services.AddControllers();

        return builder;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration config)
    {
        services.Configure<DatabaseOptions>(config.GetSection("Database"));
        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        services.Configure<RedisOptions>(config.GetSection("Redis"));
        services.Configure<CorsOptions>(config.GetSection("Cors"));
        services.Configure<RateLimitingOptions>(config.GetSection("RateLimiting"));
        services.Configure<AdminOptions>(config.GetSection("Admin"));
        services.Configure<CloudflareOptions>(config.GetSection("Cloudflare"));
        services.Configure<LinodeOptions>(config.GetSection("Linode"));
        services.Configure<Route53Options>(config.GetSection("Route53"));
        services.Configure<DockerOptions>(config.GetSection("Docker"));
        services.Configure<CaddyOptions>(config.GetSection("Caddy"));
        services.Configure<HubEmailOptions>(config.GetSection("Email"));
        services.Configure<EmailOptions>(config.GetSection("Email"));
        services.Configure<MinioOptions>(config.GetSection(MinioOptions.SectionName));
        services.Configure<CaptchaOptions>(config.GetSection("Captcha"));
        services.Configure<AuthOptions>(config.GetSection(AuthOptions.SectionName));
        services.Configure<TopologyOptions>(config.GetSection(TopologyOptions.SectionName));
        services.Configure<ColdStorageOptions>(config.GetSection(ColdStorageOptions.SectionName));
    }

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

    private static void AddCaptcha(IServiceCollection services, IConfiguration config)
    {
        var captchaEnabled = config.GetValue<bool>("Captcha:Enabled", true);
        if (captchaEnabled)
            services.AddScoped<ICaptchaService, CaptchaService>();
        else
            services.AddSingleton<ICaptchaService, NoOpCaptchaService>();
    }

    private static void AddColdStorage(IServiceCollection services, IConfiguration config)
    {
        var coldStorageEndpoint = config.GetSection("ColdStorage:Endpoint").Value;
        if (!string.IsNullOrEmpty(coldStorageEndpoint))
            services.AddSingleton<IColdStorageService, S3ColdStorageService>();
        else
            services.AddSingleton<IColdStorageService, NoopColdStorageService>();
    }

    private static void AddJwt(IServiceCollection services, IConfiguration config)
    {
        // Verify required JWT options are configured at startup (fail-fast)
        _ = config.GetSection("Jwt:Issuer").Value
            ?? throw new InvalidOperationException("JWT issuer not configured");
        _ = config.GetSection("Jwt:Audience").Value
            ?? throw new InvalidOperationException("JWT audience not configured");

        // RsaKeySingleton holds the loaded public key for JWT validation.
        // Populated by BootstrapService at startup after the key pair is ensured to exist.
        services.AddSingleton<RsaKeySingleton>();

        // JwtService is scoped because it depends on the scoped HubDbContext
        // (private key is loaded from the SystemSettings table on demand).
        services.AddScoped<IJwtService, JwtService>();
    }

    private static void AddHttpClients(IServiceCollection services, IConfiguration config)
    {
        var dockerSocketProxyUrl = config.GetValue<string>("Docker:SocketProxyUrl") ?? "http://docker-socket-proxy:2375";
        var caddyAdminUrl = config.GetValue<string>("Caddy:AdminUrl") ?? "http://caddy:2019";
        var cloudflareApiToken = config.GetValue<string>("Cloudflare:ApiToken") ?? string.Empty;

        services.AddHttpClient("DockerSocketProxy", client =>
        {
            client.BaseAddress = new Uri(dockerSocketProxyUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHttpClient("CaddyAdmin", client =>
        {
            client.BaseAddress = new Uri(caddyAdminUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddHttpClient("Cloudflare", client =>
        {
            client.BaseAddress = new Uri("https://api.cloudflare.com");
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {cloudflareApiToken}");
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        var linodeApiToken = config.GetValue<string>("Linode:ApiToken") ?? string.Empty;
        services.AddHttpClient("Linode", client =>
        {
            client.BaseAddress = new Uri("https://api.linode.com/v4");
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {linodeApiToken}");
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // MinIO - root client and provisioning service
        var minioOptions = config.GetSection(MinioOptions.SectionName).Get<MinioOptions>() ?? new MinioOptions();
        var minioEndpoint = minioOptions.Endpoint;
        if (minioEndpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            minioEndpoint = minioEndpoint[7..];
        else if (minioEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            minioEndpoint = minioEndpoint[8..];

        services.AddMinio(configure =>
        {
            configure
                .WithEndpoint(minioEndpoint)
                .WithCredentials(minioOptions.AccessKey, minioOptions.SecretKey)
                .WithSSL(minioOptions.UseSsl);
        });

        // MinIO Admin REST API client (SigV4-signed, for IAM user/policy management)
        var minioAdminUrl = minioOptions.Endpoint.Contains("://")
            ? minioOptions.Endpoint
            : $"http{(minioOptions.UseSsl ? "s" : "")}://{minioOptions.Endpoint}";

        services.AddHttpClient("MinioAdmin", client =>
        {
            client.BaseAddress = new Uri(minioAdminUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
        }).AddHttpMessageHandler(() =>
            new MinioSigV4Handler(minioOptions.AccessKey, minioOptions.SecretKey));

        services.AddSingleton<IMinioProvisioningService, MinioProvisioningService>();

        // Instance notifier
        services.AddHttpClient<IInstanceNotifier, HttpInstanceNotifier>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        // Health monitoring - disable auto-redirect so HTTP health checks in dev
        // don't follow Caddy's 308 redirect to HTTPS
        services.AddHttpClient<IHealthCheckVerifier, HttpHealthCheckVerifier>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
            });
        var alertWebhookUrl = config.GetSection("Alerting:WebhookUrl").Value;
        services.AddHttpClient<IAlertService, WebhookAlertService>(client =>
        {
            // Configure HTTP client if needed
        })
        .AddTypedClient((httpClient, sp) =>
        {
            var logger = sp.GetRequiredService<ILogger<WebhookAlertService>>();
            return new WebhookAlertService(httpClient, logger, alertWebhookUrl);
        });
    }

    private static void AddProvisioning(IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<IProvisioningQueue, DatabaseProvisioningQueue>();

        // Always use real Docker and Caddy - no noop services in any environment.
        // Tests that need to override can replace via DI in their test fixtures.
        services.AddSingleton<IDockerService, HttpDockerService>();
        services.AddSingleton<ICaddyProxyManager, CaddyProxyManager>();

        var dnsProvider = config.GetValue<string>("Dns:Provider", "noop");
        switch (dnsProvider.ToLowerInvariant())
        {
            case "cloudflare":
                services.AddSingleton<IDnsProvider, CloudflareDnsProvider>();
                break;
            case "linode":
                services.AddSingleton<IDnsProvider, LinodeDnsProvider>();
                break;
            case "route53":
                services.AddSingleton<IDnsProvider, Route53DnsProvider>();
                break;
            default:
                services.AddSingleton<IDnsProvider, NoopDnsProvider>();
                break;
        }

        services.AddSingleton<TopologyResolver>();

        // Provisioning pipeline steps
        services.AddScoped<IProvisioningStep, ValidateSubdomainStep>();
        services.AddScoped<IProvisioningStep, EnforceTierLimitsStep>();
        services.AddScoped<IProvisioningStep, GenerateSecretsStep>();
        services.AddScoped<IProvisioningStep, ResolvePlacementStep>();
        services.AddScoped<IProvisioningStep, AllocateWorkerIdStep>();
        services.AddScoped<IProvisioningStep, CreateNetworkStep>();
        services.AddScoped<IProvisioningStep, ProvisionDatabaseStep>();
        services.AddScoped<IProvisioningStep, ProvisionRedisAclStep>();
        services.AddScoped<IProvisioningStep, ProvisionMinioStep>();
        services.AddScoped<IProvisioningStep, StartApiContainerStep>();
        services.AddScoped<IProvisioningStep, ConfigureDnsAndProxyStep>();
        services.AddScoped<IProvisioningStep, ConfigureBackupPolicyStep>();
        services.AddScoped<IProvisioningStep, CreateSubscriptionStep>();

        // Provisioning pipeline
        services.AddScoped<ProvisioningPipeline>();

        // Destruction pipeline steps (reverse order of provisioning)
        services.AddScoped<IDestructionStep, StopContainerStep>();
        services.AddScoped<IDestructionStep, RemoveProxyRouteStep>();
        services.AddScoped<IDestructionStep, RemoveDnsRecordStep>();
        services.AddScoped<IDestructionStep, RemoveContainerStep>();
        services.AddScoped<IDestructionStep, RemoveSecretStep>();
        services.AddScoped<IDestructionStep, RemoveNetworkStep>();
        services.AddScoped<IDestructionStep, RemoveMinioBucketStep>();
        services.AddScoped<IDestructionStep, DropDatabaseStep>();
        services.AddScoped<IDestructionStep, ReleaseRedisSlotStep>();
        services.AddScoped<IDestructionStep, RemoveRedisAclStep>();

        // Destruction pipeline
        services.AddScoped<DestructionPipeline>();
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

    private static void AddRateLimiting(IServiceCollection services, IConfiguration config)
    {
        var rateLimitOptions = config.GetSection("RateLimiting").Get<RateLimitingOptions>()
            ?? new RateLimitingOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetTokenBucketLimiter(ipAddress, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = rateLimitOptions.TokenLimit,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(rateLimitOptions.ReplenishmentPeriodSeconds),
                    TokensPerPeriod = rateLimitOptions.TokensPerPeriod,
                    AutoReplenishment = true
                });
            });

            // Registration: configurable per-IP limit (default 3/min)
            options.AddFixedWindowLimiter("auth-register", limiterOptions =>
            {
                limiterOptions.PermitLimit = rateLimitOptions.AuthRegisterPermitLimit;
                limiterOptions.Window = TimeSpan.FromMinutes(1);
                limiterOptions.QueueLimit = 0;
            });

            // Password reset: configurable per-IP limit (default 3/min)
            options.AddFixedWindowLimiter("auth-forgot-password", limiterOptions =>
            {
                limiterOptions.PermitLimit = rateLimitOptions.AuthForgotPasswordPermitLimit;
                limiterOptions.Window = TimeSpan.FromMinutes(1);
                limiterOptions.QueueLimit = 0;
            });

            // Contact form: configurable per-IP limit (default 3/min)
            options.AddFixedWindowLimiter("contact-form", limiterOptions =>
            {
                limiterOptions.PermitLimit = rateLimitOptions.ContactFormPermitLimit;
                limiterOptions.Window = TimeSpan.FromMinutes(1);
                limiterOptions.QueueLimit = 0;
            });
        });
    }

    private static readonly string[] MobileOrigins = ["capacitor://localhost", "https://localhost"];

    private static void AddCors(IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        var corsOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        if (corsOrigins.Length == 0 && !env.IsDevelopment())
            throw new InvalidOperationException("Cors:AllowedOrigins must not be empty outside Development");

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                if (corsOrigins.Length > 0)
                {
                    var allOrigins = corsOrigins.Concat(MobileOrigins).ToArray();
                    policy.WithOrigins(allOrigins)
                        .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
                        .WithHeaders("Authorization", "Content-Type", "X-Requested-With", "Accept", "Origin", "X-Xcord-Request")
                        .AllowCredentials();
                }
                else
                {
                    policy.AllowAnyOrigin()
                        .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
                        .WithHeaders("Authorization", "Content-Type", "X-Requested-With", "Accept", "Origin", "X-Xcord-Request");
                }
            });
        });
    }

    private static void AddAuth(IServiceCollection services, IConfiguration config)
    {
        var jwtIssuer = config.GetSection("Jwt:Issuer").Value
            ?? throw new InvalidOperationException("JWT issuer not configured");
        var jwtAudience = config.GetSection("Jwt:Audience").Value
            ?? throw new InvalidOperationException("JWT audience not configured");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Note: IssuerSigningKey is populated by BootstrapService after the
                // RsaKeySingleton has loaded the public key from the database.
                // SignatureValidator below also uses the singleton at request time so
                // validation works even before BootstrapService finishes (e.g. in tests).
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtIssuer,
                    ValidAudience = jwtAudience,
                    ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
                    ClockSkew = TimeSpan.Zero
                };
            })
            .AddScheme<AuthenticationSchemeOptions, FederationAuthenticationHandler>(
                FederationAuthenticationHandler.SchemeName, null);

        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.User, policy => policy
                .RequireAuthenticatedUser());

            options.AddPolicy(Policies.Admin, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim("admin", "true"));

            options.AddPolicy(Policies.Federation, policy =>
                policy.AddAuthenticationSchemes(FederationAuthenticationHandler.SchemeName)
                      .RequireAuthenticatedUser()
                      .RequireClaim("token_type", "federation"));
        });
    }

    public static async Task SeedAdminAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var encryptionService = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
        var snowflakeGenerator = scope.ServiceProvider.GetRequiredService<SnowflakeIdGenerator>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var authOptions = scope.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value;

        var adminUsername = configuration.GetSection("Admin:Username").Value;
        var adminEmail = configuration.GetSection("Admin:Email").Value;
        var adminPassword = configuration.GetSection("Admin:Password").Value;

        if (string.IsNullOrWhiteSpace(adminUsername) ||
            string.IsNullOrWhiteSpace(adminEmail) ||
            string.IsNullOrWhiteSpace(adminPassword))
        {
            return; // No admin config, skip seeding
        }

        // Check if admin user already exists
        var emailHash = encryptionService.ComputeHmac(adminEmail.ToLowerInvariant());
        var existingAdmin = await dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.EmailHash == emailHash);

        if (existingAdmin != null)
        {
            return; // Admin already exists
        }

        // Create admin user - offloaded to thread pool to avoid starvation
        var passwordHash = await Task.Run(() => BCrypt.Net.BCrypt.HashPassword(adminPassword, authOptions.BcryptWorkFactor));
        var encryptedEmail = encryptionService.Encrypt(adminEmail.ToLowerInvariant());
        var now = DateTimeOffset.UtcNow;

        var adminUser = new HubUser
        {
            Id = snowflakeGenerator.NextId(),
            Username = adminUsername,
            DisplayName = adminUsername,
            Email = encryptedEmail,
            EmailHash = emailHash,
            PasswordHash = passwordHash,
            IsAdmin = true,
            IsDisabled = false,
            CreatedAt = now,
            LastLoginAt = now
        };

        dbContext.HubUsers.Add(adminUser);
        await dbContext.SaveChangesAsync();

        Log.Information("Admin user '{Username}' created successfully", adminUsername);
    }

    /// <summary>
    /// Verifies that all required Stripe prices exist for each tier/media combination.
    /// Creates any missing prices as recurring monthly prices with the correct amount.
    /// Skipped when Stripe is not configured.
    /// </summary>
    public static async Task EnsureStripePricesAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var stripeOptions = scope.ServiceProvider.GetRequiredService<IOptions<StripeOptions>>().Value;

        if (!stripeOptions.IsConfigured)
        {
            Log.Information("Stripe not configured, skipping price verification");
            return;
        }

        Stripe.StripeConfiguration.ApiKey = stripeOptions.SecretKey;

        var tiers = new[] { InstanceTier.Free, InstanceTier.Basic, InstanceTier.Pro, InstanceTier.Enterprise };
        var priceService = new Stripe.PriceService();
        var productService = new Stripe.ProductService();

        // Ensure a product exists for Xcord hosting
        string productId;
        try
        {
            var products = await productService.ListAsync(new Stripe.ProductListOptions { Limit = 100 });
            var existing = products.Data.FirstOrDefault(p => p.Metadata.ContainsKey("xcord_hosting") && p.Active);
            if (existing != null)
            {
                productId = existing.Id;
            }
            else
            {
                var product = await productService.CreateAsync(new Stripe.ProductCreateOptions
                {
                    Name = "Xcord Hosting",
                    Metadata = new Dictionary<string, string> { ["xcord_hosting"] = "true" }
                });
                productId = product.Id;
                Log.Information("Created Stripe product {ProductId} for Xcord hosting", productId);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to verify Stripe product, skipping price verification");
            return;
        }

        foreach (var tier in tiers)
        {
            foreach (var mediaEnabled in new[] { false, true })
            {
                var priceCents = TierDefaults.GetTotalPriceCents(tier, mediaEnabled);
                if (priceCents == 0 && !mediaEnabled) continue; // Skip free without media

                var lookupKey = BuildStripePriceId(tier, mediaEnabled);

                try
                {
                    // Check if a price with this lookup key already exists
                    var existing = await priceService.ListAsync(new Stripe.PriceListOptions
                    {
                        LookupKeys = new List<string> { lookupKey },
                        Limit = 1
                    });

                    if (existing.Data.Count > 0)
                        continue; // Price exists

                    // Create the price with a lookup key
                    var suffix = mediaEnabled ? " + Media" : "";
                    var created = await priceService.CreateAsync(new Stripe.PriceCreateOptions
                    {
                        Product = productId,
                        Currency = "usd",
                        UnitAmount = priceCents,
                        Recurring = new Stripe.PriceRecurringOptions { Interval = "month" },
                        LookupKey = lookupKey,
                        TransferLookupKey = true,
                        Nickname = $"{tier}{suffix}",
                        Metadata = new Dictionary<string, string>
                        {
                            ["tier"] = tier.ToString(),
                            ["mediaEnabled"] = mediaEnabled.ToString().ToLowerInvariant()
                        }
                    });
                    Log.Information("Created Stripe price {LookupKey} -> {PriceId} ({Amount} cents/mo)",
                        lookupKey, created.Id, priceCents);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to ensure Stripe price {LookupKey}", lookupKey);
                }
            }
        }

        // Ensure the Enterprise metered price (usage-based, per-minute via Billing Meter).
        // This price is used for Enterprise instances that choose metered billing.
        // Rate: 1 cent per minute ($0.01/min = $0.60/hr ~= $432/mo at 100% uptime).
        // The price references a BillingMeter named "xcord_instance_uptime_minutes"
        // which must be set up in the Stripe dashboard (metered billing requires a meter).
        const string enterpriseMeteredLookupKey = "price_xcord_enterprise_metered";
        try
        {
            var existingMetered = await priceService.ListAsync(new Stripe.PriceListOptions
            {
                LookupKeys = new List<string> { enterpriseMeteredLookupKey },
                Limit = 1
            });

            if (existingMetered.Data.Count == 0)
            {
                // Attempt to look up the meter ID for "xcord_instance_uptime_minutes"
                string? meterId = null;
                try
                {
                    var meterService = new Stripe.Billing.MeterService();
                    var meters = await meterService.ListAsync(new Stripe.Billing.MeterListOptions { Limit = 100 });
                    meterId = meters.Data.FirstOrDefault(m => m.EventName == "xcord_instance_uptime_minutes")?.Id;
                }
                catch (Exception meterEx)
                {
                    Log.Warning(meterEx, "Could not retrieve Stripe meters - Enterprise metered price requires a billing meter to be configured manually");
                }

                if (meterId != null)
                {
                    var created = await priceService.CreateAsync(new Stripe.PriceCreateOptions
                    {
                        Product = productId,
                        Currency = "usd",
                        UnitAmount = 1, // 1 cent per unit (minute)
                        Recurring = new Stripe.PriceRecurringOptions
                        {
                            Interval = "month",
                            Meter = meterId,
                            UsageType = "metered"
                        },
                        LookupKey = enterpriseMeteredLookupKey,
                        TransferLookupKey = true,
                        Nickname = "Enterprise Metered (per minute)",
                        Metadata = new Dictionary<string, string>
                        {
                            ["tier"] = "Enterprise",
                            ["billing_type"] = "metered",
                            ["unit"] = "minute"
                        }
                    });
                    Log.Information("Created Stripe metered price {LookupKey} -> {PriceId}",
                        enterpriseMeteredLookupKey, created.Id);
                }
                else
                {
                    Log.Warning(
                        "Stripe metered price {LookupKey} not created: configure a billing meter named " +
                        "'xcord_instance_uptime_minutes' in the Stripe dashboard first",
                        enterpriseMeteredLookupKey);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to ensure Stripe Enterprise metered price {LookupKey}", enterpriseMeteredLookupKey);
        }

        Log.Information("Stripe price verification completed");
    }

    private static string BuildStripePriceId(InstanceTier tier, bool mediaEnabled)
    {
        var suffix = mediaEnabled ? "_media" : "";
        return $"price_xcord_{tier.ToString().ToLowerInvariant()}{suffix}";
    }
}
