using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BCrypt.Net;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Minio;
using Serilog;
using StackExchange.Redis;
using XcordHub.Api.Options;
using XcordHub.Entities;
using XcordHub.Features;
using XcordHub.Features.Auth;
using XcordHub.Features.Monitoring;
using XcordHub.Features.Destruction;
using XcordHub.Features.Provisioning;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;
using CloudflareOptions = XcordHub.Infrastructure.Services.CloudflareOptions;
using DockerOptions = XcordHub.Infrastructure.Services.DockerOptions;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace XcordHub.Api;

public static class ServiceCollectionExtensions
{
    public static WebApplicationBuilder AddHubServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        // JSON serialization — explicit camelCase + converters
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
        services.AddSingleton(sp => new SnowflakeId(1)); // workerId 1 for hub

        // Encryption
        AddEncryption(services, config);

        // Captcha
        AddCaptcha(services, config);

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

        // Background services
        services.AddHostedService<ProvisioningBackgroundService>();
        services.AddHostedService<HealthCheckMonitor>();
        services.AddHostedService<InstanceReconciler>();

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

        // Request handlers
        services.AddRequestHandlers(typeof(FeaturesAssemblyMarker).Assembly);
        services.AddScoped<RefreshTokenHandler>();

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
        services.Configure<DockerOptions>(config.GetSection("Docker"));
        services.Configure<CaddyOptions>(config.GetSection("Caddy"));
        services.Configure<EmailOptions>(config.GetSection("Email"));
        services.Configure<MinioOptions>(config.GetSection(MinioOptions.SectionName));
        services.Configure<CaptchaOptions>(config.GetSection("Captcha"));
        services.Configure<AuthOptions>(config.GetSection(AuthOptions.SectionName));
    }

    private static void AddEncryption(IServiceCollection services, IConfiguration config)
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
                Log.Warning("Hub has KEK configured but encryption key is plaintext — wrap the key for production use");
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

            resolvedEncryptionKey = encryptionKeyRaw
                ?? throw new InvalidOperationException("Encryption key not configured");
            Log.Warning("Hub encryption key loaded WITHOUT envelope encryption — configure a KEK for production use");
        }
        services.AddSingleton<IEncryptionService>(new AesEncryptionService(resolvedEncryptionKey));
    }

    private static void AddCaptcha(IServiceCollection services, IConfiguration config)
    {
        var captchaEnabled = config.GetValue<bool>("Captcha:Enabled", true);
        if (captchaEnabled)
            services.AddScoped<ICaptchaService, CaptchaService>();
        else
            services.AddSingleton<ICaptchaService, NoOpCaptchaService>();
    }

    private static void AddJwt(IServiceCollection services, IConfiguration config)
    {
        var jwtSecretKey = config.GetSection("Jwt:SecretKey").Value
            ?? throw new InvalidOperationException("JWT secret key not configured");
        var jwtIssuer = config.GetSection("Jwt:Issuer").Value
            ?? throw new InvalidOperationException("JWT issuer not configured");
        var jwtAudience = config.GetSection("Jwt:Audience").Value
            ?? throw new InvalidOperationException("JWT audience not configured");
        var jwtExpirationMinutes = config.GetValue<int>("Jwt:ExpirationMinutes", 15);
        services.AddSingleton<IJwtService>(new JwtService(jwtIssuer, jwtAudience, jwtSecretKey, jwtExpirationMinutes));
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

        // MinIO — root client and provisioning service
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

        var minioConsoleEndpoint = minioOptions.ConsoleEndpoint;
        var minioConsoleUrl = minioConsoleEndpoint.Contains("://")
            ? minioConsoleEndpoint
            : $"http{(minioOptions.UseSsl ? "s" : "")}://{minioConsoleEndpoint}";

        services.AddHttpClient("MinioConsole", client =>
        {
            client.BaseAddress = new Uri(minioConsoleUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddSingleton<IMinioProvisioningService, MinioProvisioningService>();

        // Instance notifier
        services.AddHttpClient<IInstanceNotifier, HttpInstanceNotifier>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        // Health monitoring
        services.AddHttpClient<IHealthCheckVerifier, HttpHealthCheckVerifier>();
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

        var useRealDocker = config.GetValue<bool>("Docker:UseReal", false);
        var useRealDns = config.GetValue<bool>("Cloudflare:UseReal", false);
        var useRealCaddy = config.GetValue<bool>("Caddy:UseReal", false);

        if (useRealDocker)
            services.AddSingleton<IDockerService, HttpDockerService>();
        else
            services.AddSingleton<IDockerService, NoopDockerService>();

        if (useRealDns)
            services.AddSingleton<IDnsProvider, CloudflareDnsProvider>();
        else
            services.AddSingleton<IDnsProvider, NoopDnsProvider>();

        if (useRealCaddy)
            services.AddSingleton<ICaddyProxyManager, CaddyProxyManager>();
        else
            services.AddSingleton<ICaddyProxyManager, NoopCaddyProxyManager>();

        // Provisioning pipeline steps
        services.AddScoped<IProvisioningStep, ValidateSubdomainStep>();
        services.AddScoped<IProvisioningStep, EnforceTierLimitsStep>();
        services.AddScoped<IProvisioningStep, GenerateSecretsStep>();
        services.AddScoped<IProvisioningStep, AllocateWorkerIdStep>();
        services.AddScoped<IProvisioningStep, CreateNetworkStep>();
        services.AddScoped<IProvisioningStep, ProvisionDatabaseStep>();
        services.AddScoped<IProvisioningStep, ProvisionMinioStep>();
        services.AddScoped<IProvisioningStep, StartApiContainerStep>();
        services.AddScoped<IProvisioningStep, ConfigureDnsAndProxyStep>();

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
                        .WithHeaders("Authorization", "Content-Type", "X-Requested-With", "Accept", "Origin")
                        .AllowCredentials();
                }
                else
                {
                    policy.AllowAnyOrigin()
                        .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
                        .WithHeaders("Authorization", "Content-Type", "X-Requested-With", "Accept", "Origin");
                }
            });
        });
    }

    private static void AddAuth(IServiceCollection services, IConfiguration config)
    {
        var jwtSecretKey = config.GetSection("Jwt:SecretKey").Value
            ?? throw new InvalidOperationException("JWT secret key not configured");
        var jwtIssuer = config.GetSection("Jwt:Issuer").Value
            ?? throw new InvalidOperationException("JWT issuer not configured");
        var jwtAudience = config.GetSection("Jwt:Audience").Value
            ?? throw new InvalidOperationException("JWT audience not configured");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var key = Encoding.UTF8.GetBytes(jwtSecretKey);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtIssuer,
                    ValidAudience = jwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ClockSkew = TimeSpan.Zero
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.User, policy => policy
                .RequireAuthenticatedUser());

            options.AddPolicy(Policies.Admin, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim("admin", "true"));
        });
    }

    public static async Task SeedAdminAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var encryptionService = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
        var snowflakeGenerator = scope.ServiceProvider.GetRequiredService<SnowflakeId>();
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

        // Create admin user — offloaded to thread pool to avoid starvation
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
}
