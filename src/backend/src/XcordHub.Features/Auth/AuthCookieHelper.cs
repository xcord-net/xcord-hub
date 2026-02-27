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

        return (SameSiteMode.Strict, httpContext.Request.IsHttps);
    }
}
