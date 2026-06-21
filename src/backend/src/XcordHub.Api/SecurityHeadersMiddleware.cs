namespace XcordHub.Api;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            // Fonts are self-hosted (@fontsource), so style-src/font-src no longer
            // need the Google Fonts CDN. Stripe/Google Pay allowances are retained.
            headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' https://js.stripe.com; style-src 'self'; connect-src 'self' wss: https://api.stripe.com; img-src 'self' blob:; font-src 'self'; frame-src https://js.stripe.com https://pay.google.com; frame-ancestors 'none'";
            headers["X-XSS-Protection"] = "0";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

            return Task.CompletedTask;
        });

        await _next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
