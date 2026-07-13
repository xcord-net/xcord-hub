using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Minio;
using Xcord.Captcha;
using Xcord.Captcha.AspNetCore;
using XcordHub.Api.Options;
using XcordHub.Features.Backups;
using XcordHub.Features.Monitoring;
using XcordHub.Features.Provisioning;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;
using CloudflareOptions = XcordHub.Infrastructure.Services.CloudflareOptions;
using DockerOptions = XcordHub.Infrastructure.Services.DockerOptions;
using LinodeOptions = XcordHub.Infrastructure.Services.LinodeOptions;
using Route53Options = XcordHub.Infrastructure.Services.Route53Options;

namespace XcordHub.Api;

public static partial class ServiceCollectionExtensions
{
    private static readonly string[] MobileOrigins = ["capacitor://localhost", "https://localhost"];

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
        services.Configure<AuthOptions>(config.GetSection(AuthOptions.SectionName));
        services.Configure<TopologyOptions>(config.GetSection(TopologyOptions.SectionName));
        services.Configure<ColdStorageOptions>(config.GetSection(ColdStorageOptions.SectionName));
    }

    private static void AddCaptcha(IServiceCollection services, IConfiguration config)
    {
        var enabled = config.GetValue<bool>("Captcha:Enabled", true);
        services.AddGhostFontCaptcha(o =>
        {
            o.Enabled = enabled;
            o.KeyPrefix = "captcha:"; // hub shares Redis; keep existing key shape
        });
        if (enabled) services.UseRedisCaptchaStore();
    }

    private static void AddColdStorage(IServiceCollection services, IConfiguration config)
    {
        var coldStorageEndpoint = config.GetSection("ColdStorage:Endpoint").Value;
        if (!string.IsNullOrEmpty(coldStorageEndpoint))
            services.AddSingleton<IColdStorageService, S3ColdStorageService>();
        else
            services.AddSingleton<IColdStorageService, NoopColdStorageService>();
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

            // Captcha issuance: per-IP limit (default 20/min) to slow mass GIF harvesting.
            // Uses a policy (per-IP partition) rather than AddFixedWindowLimiter, which shares
            // one bucket across all callers.
            options.AddPolicy("captcha", context =>
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.CaptchaPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            });

            // Federation bootstrap-token registration: tight per-IP limit (default 5 / 15 min).
            // Protects /api/v1/federation/register against brute-force token guessing. Uses a
            // policy rather than AddFixedWindowLimiter so the bucket is partitioned by client
            // IP, not shared globally across all callers like the other named limiters above.
            options.AddPolicy("BootstrapToken", context =>
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.BootstrapTokenPermitLimit,
                    Window = TimeSpan.FromMinutes(rateLimitOptions.BootstrapTokenWindowMinutes),
                    QueueLimit = 0
                });
            });
        });
    }

    private static void AddCors(IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        var corsOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

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
                    // Federation peers form an unbounded set of origins; the hub also serves
                    // browser traffic from arbitrary instances during cross-instance discovery
                    // and onboarding flows. The default policy therefore permits any origin.
                    // Deployments that want a tighter policy can set Cors:AllowedOrigins.
                    policy.AllowAnyOrigin()
                        .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
                        .WithHeaders("Authorization", "Content-Type", "X-Requested-With", "Accept", "Origin", "X-Xcord-Request");
                }
            });
        });
    }
}
