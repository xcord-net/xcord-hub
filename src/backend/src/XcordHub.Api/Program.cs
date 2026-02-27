using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;
using XcordHub.Api;
using XcordHub.Features;
using XcordHub.Infrastructure.Data;

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

// Expose Program to test projects that use WebApplicationFactory<Program>.
public partial class Program { }
