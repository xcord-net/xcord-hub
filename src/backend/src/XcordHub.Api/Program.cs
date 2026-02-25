using System.Text;
using System.Threading.RateLimiting;
using BCrypt.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Minio;
using Serilog;
using StackExchange.Redis;
using XcordHub.Api;
using XcordHub.Api.Options;
using XcordHub.Entities;
using XcordHub.Features;
using XcordHub.Features.Monitoring;
using XcordHub.Features.Provisioning;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;
using XcordHub.Infrastructure.Data;
using XcordHub;
using CloudflareOptions = XcordHub.Infrastructure.Services.CloudflareOptions;
using DockerOptions = XcordHub.Infrastructure.Services.DockerOptions;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "XcordHub.Gateway")
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Options
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection("Database"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection("Redis"));
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection("Cors"));
builder.Services.Configure<RateLimitingOptions>(builder.Configuration.GetSection("RateLimiting"));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection("Admin"));
builder.Services.Configure<CloudflareOptions>(builder.Configuration.GetSection("Cloudflare"));
builder.Services.Configure<DockerOptions>(builder.Configuration.GetSection("Docker"));
builder.Services.Configure<CaddyOptions>(builder.Configuration.GetSection("Caddy"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<MinioOptions>(builder.Configuration.GetSection(MinioOptions.SectionName));

// Database
var connectionString = builder.Configuration.GetSection("Database:ConnectionString").Value
    ?? throw new InvalidOperationException("Database connection string not configured");

builder.Services.AddDbContext<HubDbContext>(options =>
    options.UseNpgsql(connectionString));

// Snowflake ID generator
builder.Services.AddSingleton(sp => new SnowflakeId(1)); // workerId 1 for hub

// Encryption — resolve KEK, then DEK
builder.Services.AddSingleton<IKekProvider, FileKekProvider>();

// Resolve KEK inline (before DI container is built)
byte[]? hubKek = null;
{
    var kekFile = builder.Configuration.GetSection("Encryption:KekFile").Value ?? "/run/secrets/xcord-kek";
    var kekBase64 = builder.Configuration.GetSection("Encryption:Kek").Value;
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
var encryptionKeyRaw = builder.Configuration.GetSection("Encryption:Key").Value;
var wrappedKeyRaw = builder.Configuration.GetSection("Encryption:WrappedKey").Value;

string resolvedEncryptionKey;
if (hubKek != null)
{
    if (!string.IsNullOrEmpty(wrappedKeyRaw))
    {
        // Unwrap the wrapped DEK
        var wrappedBytes = Convert.FromBase64String(wrappedKeyRaw);
        var dekBytes = KeyWrappingService.UnwrapDek(wrappedBytes, hubKek);
        resolvedEncryptionKey = Convert.ToBase64String(dekBytes);
        Log.Information("Hub encryption key unwrapped using KEK");
    }
    else if (!string.IsNullOrEmpty(encryptionKeyRaw))
    {
        // Plaintext key + KEK — use plaintext for now, log warning
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
builder.Services.AddSingleton<IEncryptionService>(new AesEncryptionService(resolvedEncryptionKey));

// Email
builder.Services.AddScoped<IEmailService, SmtpEmailService>();

// Stripe billing
builder.Services.Configure<XcordHub.Infrastructure.Options.StripeOptions>(
    builder.Configuration.GetSection(XcordHub.Infrastructure.Options.StripeOptions.SectionName));
builder.Services.AddScoped<IStripeService, XcordHub.Infrastructure.Services.StripeService>();
builder.Services.AddScoped<XcordHub.Features.Billing.StripeWebhookHandler>();

// JWT
var jwtSecretKey = builder.Configuration.GetSection("Jwt:SecretKey").Value
    ?? throw new InvalidOperationException("JWT secret key not configured");
var jwtIssuer = builder.Configuration.GetSection("Jwt:Issuer").Value
    ?? throw new InvalidOperationException("JWT issuer not configured");
var jwtAudience = builder.Configuration.GetSection("Jwt:Audience").Value
    ?? throw new InvalidOperationException("JWT audience not configured");
var jwtExpirationMinutes = builder.Configuration.GetValue<int>("Jwt:ExpirationMinutes", 15);
builder.Services.AddSingleton<IJwtService>(new JwtService(jwtIssuer, jwtAudience, jwtSecretKey, jwtExpirationMinutes));

// HttpClient factory for infrastructure services
var dockerSocketProxyUrl = builder.Configuration.GetValue<string>("Docker:SocketProxyUrl") ?? "http://docker-socket-proxy:2375";
var caddyAdminUrl = builder.Configuration.GetValue<string>("Caddy:AdminUrl") ?? "http://caddy:2019";
var cloudflareApiToken = builder.Configuration.GetValue<string>("Cloudflare:ApiToken") ?? string.Empty;

builder.Services.AddHttpClient("DockerSocketProxy", client =>
{
    client.BaseAddress = new Uri(dockerSocketProxyUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHttpClient("CaddyAdmin", client =>
{
    client.BaseAddress = new Uri(caddyAdminUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpClient("Cloudflare", client =>
{
    client.BaseAddress = new Uri("https://api.cloudflare.com");
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {cloudflareApiToken}");
    client.Timeout = TimeSpan.FromSeconds(15);
});

// MinIO — root client and provisioning service
var minioOptions = builder.Configuration.GetSection(MinioOptions.SectionName).Get<MinioOptions>() ?? new MinioOptions();
var minioEndpoint = minioOptions.Endpoint;
if (minioEndpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
    minioEndpoint = minioEndpoint[7..];
else if (minioEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    minioEndpoint = minioEndpoint[8..];

builder.Services.AddMinio(configure =>
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

builder.Services.AddHttpClient("MinioConsole", client =>
{
    client.BaseAddress = new Uri(minioConsoleUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddSingleton<IMinioProvisioningService, MinioProvisioningService>();

// Provisioning infrastructure services
builder.Services.AddScoped<IProvisioningQueue, DatabaseProvisioningQueue>();

// Use environment variable to switch between real and noop implementations
var useRealDocker = builder.Configuration.GetValue<bool>("Docker:UseReal", false);
var useRealDns = builder.Configuration.GetValue<bool>("Cloudflare:UseReal", false);
var useRealCaddy = builder.Configuration.GetValue<bool>("Caddy:UseReal", false);

if (useRealDocker)
{
    builder.Services.AddSingleton<IDockerService, HttpDockerService>();
}
else
{
    builder.Services.AddSingleton<IDockerService, NoopDockerService>();
}

if (useRealDns)
{
    builder.Services.AddSingleton<IDnsProvider, CloudflareDnsProvider>();
}
else
{
    builder.Services.AddSingleton<IDnsProvider, NoopDnsProvider>();
}

if (useRealCaddy)
{
    builder.Services.AddSingleton<ICaddyProxyManager, CaddyProxyManager>();
}
else
{
    builder.Services.AddSingleton<ICaddyProxyManager, NoopCaddyProxyManager>();
}

// Instance notifier (sends System_ShuttingDown before stopping containers)
builder.Services.AddHttpClient<IInstanceNotifier, HttpInstanceNotifier>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(5); // outer timeout; notifier uses 4s inner CTS
});

// Health monitoring services
builder.Services.AddHttpClient<IHealthCheckVerifier, HttpHealthCheckVerifier>();
var alertWebhookUrl = builder.Configuration.GetSection("Alerting:WebhookUrl").Value;
builder.Services.AddHttpClient<IAlertService, WebhookAlertService>(client =>
{
    // Configure HTTP client if needed
})
.AddTypedClient((httpClient, sp) =>
{
    var logger = sp.GetRequiredService<ILogger<WebhookAlertService>>();
    return new WebhookAlertService(httpClient, logger, alertWebhookUrl);
});

// Provisioning pipeline steps
builder.Services.AddScoped<IProvisioningStep, ValidateSubdomainStep>();
builder.Services.AddScoped<IProvisioningStep, EnforceTierLimitsStep>();
builder.Services.AddScoped<IProvisioningStep, GenerateSecretsStep>();
builder.Services.AddScoped<IProvisioningStep, AllocateWorkerIdStep>();
builder.Services.AddScoped<IProvisioningStep, CreateNetworkStep>();
builder.Services.AddScoped<IProvisioningStep, ProvisionDatabaseStep>();
builder.Services.AddScoped<IProvisioningStep, ProvisionMinioStep>();
builder.Services.AddScoped<IProvisioningStep, StartApiContainerStep>();
builder.Services.AddScoped<IProvisioningStep, ConfigureDnsAndProxyStep>();

// Provisioning pipeline
builder.Services.AddScoped<ProvisioningPipeline>();

// Background services
builder.Services.AddHostedService<ProvisioningBackgroundService>();
builder.Services.AddHostedService<HealthCheckMonitor>();
builder.Services.AddHostedService<InstanceReconciler>();

// Metrics
builder.Services.AddSingleton<GatewayMetrics>();
builder.Services.AddSingleton<ProvisioningMetrics>();

// Redis
var redisConnectionString = builder.Configuration.GetSection("Redis:ConnectionString").Value
    ?? throw new InvalidOperationException("Redis connection string not configured");

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configurationOptions = ConfigurationOptions.Parse(redisConnectionString);
    configurationOptions.AbortOnConnectFail = false;
    configurationOptions.ConnectTimeout = 5000;
    configurationOptions.SyncTimeout = 1000;
    configurationOptions.ConnectRetry = 3;
    return ConnectionMultiplexer.Connect(configurationOptions);
});

// OpenTelemetry
builder.Services.AddOpenTelemetry()
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
builder.Services.AddHttpContextAccessor();

// Current user service
builder.Services.AddScoped<XcordHub.Infrastructure.Services.ICurrentUserService, XcordHub.Infrastructure.Services.CurrentUserService>();

// Request handlers
builder.Services.AddRequestHandlers(typeof(FeaturesAssemblyMarker).Assembly);

// Rate limiting
var rateLimitOptions = builder.Configuration.GetSection("RateLimiting").Get<RateLimitingOptions>()
    ?? new RateLimitingOptions();

builder.Services.AddRateLimiter(options =>
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
});

// CORS
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

if (corsOrigins.Length == 0 && builder.Environment.IsProduction())
{
    throw new InvalidOperationException("Cors:AllowedOrigins must not be empty in Production");
}

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
        else
        {
            // Development only: allow any origin without credentials
            policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

// JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.User, policy => policy
        .RequireAuthenticatedUser());

    options.AddPolicy(Policies.Admin, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("admin", "true"));
});

// OpenAPI
builder.Services.AddOpenApi();

// Exception handling
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Controllers
builder.Services.AddControllers();

var app = builder.Build();

// Apply database schema and seed admin (skip during OpenAPI spec generation)
if (!app.Environment.IsEnvironment("OpenApiGen"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<HubDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
    await SeedAdminUser(app);
}

// Middleware pipeline
app.UseExceptionHandler();

app.UseSerilogRequestLogging();

app.UseSecurityHeaders();

// Forwarded headers (must be before rate limiter to correctly resolve client IPs behind reverse proxy)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseRateLimiter();

if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

app.UseCors();

// Admin SPA: rewrite non-file /admin paths to admin/index.html.
// Must be BEFORE UseRouting — UseStaticFiles skips files when an endpoint is matched
// by routing, and the hub SPA fallback ({*path:nonfile}) matches /admin.
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    if (path.Equals("/admin", StringComparison.OrdinalIgnoreCase) ||
        (path.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase) &&
         !System.IO.Path.HasExtension(path)))
    {
        context.Request.Path = "/admin/index.html";
    }
    await next();
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();

app.MapControllers();

// Health endpoint
app.MapHealthEndpoint();

// Auto-register all handler endpoints
app.MapHandlerEndpoints(typeof(FeaturesAssemblyMarker).Assembly);

// Stripe webhook endpoint (non-standard handler — registered manually)
XcordHub.Features.Billing.StripeWebhookHandler.Map(app);

// OpenAPI endpoint (serves /openapi/v1.json)
app.MapOpenApi();

// Hub SPA fallback
app.MapFallbackToFile("index.html");

app.Run();

// Admin seeding
static async Task SeedAdminUser(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<HubDbContext>();
    var encryptionService = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
    var snowflakeGenerator = scope.ServiceProvider.GetRequiredService<SnowflakeId>();
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

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

    // Create admin user
    var passwordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword, 12);
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

// Expose Program to test projects that use WebApplicationFactory<Program>.
public partial class Program { }

