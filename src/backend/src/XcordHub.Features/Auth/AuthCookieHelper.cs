using Microsoft.AspNetCore.Http;

namespace XcordHub.Features.Auth;

public static class AuthCookieHelper
{
    public static void SetRefreshTokenCookie(HttpContext httpContext, string refreshToken)
    {
        var (sameSite, secure) = GetCookiePolicy(httpContext);

        httpContext.Response.Cookies.Append("refresh_token", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = sameSite,
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });
    }

    public static void DeleteRefreshTokenCookie(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete("refresh_token");
    }

    private static (SameSiteMode SameSite, bool Secure) GetCookiePolicy(HttpContext httpContext)
    {
        var origin = httpContext.Request.Headers.Origin.FirstOrDefault() ?? "";
        var isMobile = origin.StartsWith("capacitor://") || origin == "https://localhost";

        if (isMobile)
            return (SameSiteMode.None, true);

        // Behind a reverse proxy, httpContext.Request.IsHttps is false even when the client
        // connected over HTTPS (TLS terminated at the proxy). Check X-Forwarded-Proto to
        // detect the original scheme, and set Secure = true when the upstream connection
        // was HTTPS. This ensures cookies get the Secure flag in production while still
        // working in HTTP-only development environments.
        var forwardedProto = httpContext.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        var isSecure = httpContext.Request.IsHttps
                    || string.Equals(forwardedProto, "https", StringComparison.OrdinalIgnoreCase);
        return (SameSiteMode.Strict, isSecure);
    }
}
