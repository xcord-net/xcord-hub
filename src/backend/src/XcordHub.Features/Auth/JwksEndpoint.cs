using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.Tokens;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Auth;

/// <summary>
/// Public JWKS endpoint exposing the hub's RSA public key for JWT verification.
/// Federation instances and other relying parties can fetch this to validate hub-issued tokens
/// without manual key configuration.
/// </summary>
public sealed record JwksKey(
    string Kty,
    string Use,
    string Alg,
    string Kid,
    string N,
    string E);

public sealed record JwksResponse(IReadOnlyList<JwksKey> Keys);

public sealed class JwksEndpoint : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/.well-known/jwks.json", (RsaKeySingleton rsaKeys) =>
        {
            var parameters = rsaKeys.GetPublicParameters();
            var n = Base64UrlEncoder.Encode(parameters.Modulus!);
            var e = Base64UrlEncoder.Encode(parameters.Exponent!);

            var key = new JwksKey(
                Kty: "RSA",
                Use: "sig",
                Alg: "RS256",
                Kid: rsaKeys.GetKeyId(),
                N: n,
                E: e);

            return Results.Ok(new JwksResponse(new[] { key }));
        })
        .AllowAnonymous()
        .Produces<JwksResponse>(200)
        .WithName("Jwks")
        .WithTags("Auth");
    }
}
