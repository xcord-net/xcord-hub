using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using XcordHub.Api.Auth;
using XcordHub.Features.Auth;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Api;

public static partial class ServiceCollectionExtensions
{
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
}
