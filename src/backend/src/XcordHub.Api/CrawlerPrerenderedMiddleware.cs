namespace XcordHub.Api;

public sealed class CrawlerPrerenderedMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _env;

    private static readonly HashSet<string> PrerenderedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "",
        "/pricing",
        "/get-started",
        "/download",
        "/docs/self-hosting",
        "/terms",
        "/privacy",
    };

    private static readonly string[] CrawlerPatterns =
    [
        "Googlebot",
        "Bingbot",
        "Slurp",
        "DuckDuckBot",
        "Baiduspider",
        "YandexBot",
        "facebookexternalhit",
        "Twitterbot",
        "LinkedInBot",
        "Slackbot",
        "WhatsApp",
        "TelegramBot",
        "Discordbot",
        "Embedly",
        "Applebot",
        "Pinterestbot",
        "redditbot",
    ];

    public CrawlerPrerenderedMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var rawPath = context.Request.Path.Value ?? "";
        var path = rawPath.TrimEnd('/');
        if (rawPath == "/")
        {
            path = "";
        }

        if (PrerenderedPaths.Contains(path))
        {
            var userAgent = context.Request.Headers.UserAgent.ToString();
            var isCrawler = CrawlerPatterns.Any(p =>
                userAgent.Contains(p, StringComparison.OrdinalIgnoreCase));

            if (isCrawler)
            {
                var relativePath = path == ""
                    ? "/prerendered/index.html"
                    : $"/prerendered{path}/index.html";

                var fileInfo = _env.WebRootFileProvider.GetFileInfo(relativePath);
                if (fileInfo.Exists)
                {
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await using var stream = fileInfo.CreateReadStream();
                    await stream.CopyToAsync(context.Response.Body);
                    return;
                }
            }
        }

        await _next(context);
    }
}

public static class CrawlerPrerenderedMiddlewareExtensions
{
    public static IApplicationBuilder UseCrawlerPrerendered(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CrawlerPrerenderedMiddleware>();
    }
}
