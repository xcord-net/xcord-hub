using System.Text.Json.Serialization;
using XcordHub.Entities;
using XcordHub.Features;
using XcordHub.Features.Auth;
using XcordHub.Features.Backups;
using XcordHub.Features.Billing;
using XcordHub.Features.Destruction;
using XcordHub.Features.Instances;
using XcordHub.Features.Monitoring;
using XcordHub.Features.Provisioning;
using XcordHub.Features.Upgrades;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace XcordHub.Api;

/// <summary>
/// Top-level DI orchestration. The per-concern method bodies live in the
/// partial files alongside this one:
///   - ServiceCollectionExtensions.Auth.cs
///   - ServiceCollectionExtensions.Persistence.cs
///   - ServiceCollectionExtensions.Infrastructure.cs
///   - ServiceCollectionExtensions.Features.cs
/// </summary>
public static partial class ServiceCollectionExtensions
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

        // Options (Infrastructure partial)
        AddOptions(services, config);

        // Database
        var connectionString = config.GetSection("Database:ConnectionString").Value
            ?? throw new InvalidOperationException("Database connection string not configured");
        services.AddDbContext<HubDbContext>(options => options.UseNpgsql(connectionString));

        // Snowflake ID generator
        services.AddSingleton(sp => new SnowflakeIdGenerator(1)); // workerId 1 for hub

        // Encryption (Persistence partial)
        AddEncryption(services, config, builder.Environment);

        // Captcha (Infrastructure partial)
        AddCaptcha(services, config);

        // Cold storage (Infrastructure partial)
        AddColdStorage(services, config);

        // Email
        services.AddScoped<IEmailService, SmtpEmailService>();

        // Stripe billing
        services.Configure<XcordHub.Infrastructure.Options.StripeOptions>(
            config.GetSection(XcordHub.Infrastructure.Options.StripeOptions.SectionName));
        services.AddScoped<IStripeService, XcordHub.Infrastructure.Services.StripeService>();
        services.AddScoped<XcordHub.Features.Billing.StripeWebhookHandler>();

        // JWT (Auth partial)
        AddJwt(services, config);

        // HttpClient registrations (Infrastructure partial)
        AddHttpClients(services, config);

        // Provisioning (Features partial)
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

        // Redis (Persistence partial)
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

        // Rate limiting (Infrastructure partial)
        AddRateLimiting(services, config);

        // CORS (Infrastructure partial)
        AddCors(services, config, builder.Environment);

        // Authentication & Authorization (Auth partial)
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
}
