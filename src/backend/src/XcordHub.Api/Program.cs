using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;
using XcordHub.Api;
using XcordHub.Features;
using XcordHub.Infrastructure.Data;

// Pre-warm the thread pool to handle concurrent CPU-bound work (e.g. BCrypt)
// without starvation. Default min threads is too low for burst auth traffic.
ThreadPool.SetMinThreads(workerThreads: 32, completionPortThreads: 32);

var builder = WebApplication.CreateBuilder(args);

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "XcordHub.Gateway")
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Register all services
builder.AddHubServices();

var app = builder.Build();

// Apply database schema and seed admin (skip during OpenAPI spec generation)
if (!app.Environment.IsEnvironment("OpenApiGen"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<HubDbContext>();
    await dbContext.Database.MigrateAsync();
    await app.SeedAdminAsync();
    await app.EnsureStripePricesAsync();
}

// Middleware pipeline
app.UseExceptionHandler();

app.UseSerilogRequestLogging(opts =>
{
    opts.GetLevel = (httpContext, _, _) =>
        httpContext.Request.Path.StartsWithSegments("/health")
            ? Serilog.Events.LogEventLevel.Debug
            : Serilog.Events.LogEventLevel.Information;
});

app.UseSecurityHeaders();

// Forwarded headers (must be before rate limiter to correctly resolve client IPs behind reverse proxy)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseRateLimiter();

// HTTPS redirect handled by Caddy at the edge - gateway only receives HTTP from the reverse proxy
app.UseCors();

// Admin SPA: rewrite non-file /admin paths to admin/index.html.
// Must be BEFORE UseRouting - UseStaticFiles skips files when an endpoint is matched
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

app.UseCrawlerPrerendered();

app.UseStaticFiles();

app.MapControllers();

// Health endpoint
app.MapHealthEndpoint();

// Auto-register all handler endpoints
app.MapHandlerEndpoints(typeof(FeaturesAssemblyMarker).Assembly);

// Stripe webhook endpoint (non-standard handler - registered manually)
XcordHub.Features.Billing.StripeWebhookHandler.Map(app);

// Dev-only test seed endpoint for E2E tests
if (app.Environment.IsDevelopment())
{
    TestSeedEndpoint.Map(app);
}

// OpenAPI endpoint (serves /openapi/v1.json)
app.MapOpenApi();

// Hub SPA fallback
app.MapFallbackToFile("index.html");

app.Run();

// Expose Program to test projects that use WebApplicationFactory<Program>.
public partial class Program { }
