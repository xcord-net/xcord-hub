using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Header;

public sealed class GetHeaderHandler : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/header", async (
            HttpContext httpContext,
            HubDbContext db,
            string? serverUrl,
            string? hubKey,
            CancellationToken ct) =>
        {
            // Validate serverUrl is a real HTTP(S) URL (prevents SSRF to internal services)
            if (!string.IsNullOrEmpty(serverUrl) && !IsValidHttpsUrl(serverUrl))
                return Results.BadRequest("serverUrl must be an HTTP or HTTPS URL");

            // Validate hubKey format if provided (base64url, max 64 chars)
            if (!string.IsNullOrEmpty(hubKey) && !HubKeyPattern.IsMatch(hubKey))
                return Results.BadRequest("invalid hubKey format");

            // Resolve hubKey: param > cookie > generate
            var key = hubKey;
            if (string.IsNullOrEmpty(key))
            {
                var cookieKey = httpContext.Request.Cookies["xcord_hub_key"];
                if (!string.IsNullOrEmpty(cookieKey) && HubKeyPattern.IsMatch(cookieKey))
                    key = cookieKey;
            }

            var isNewKey = false;
            if (string.IsNullOrEmpty(key))
            {
                key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
                    .Replace("+", "-").Replace("/", "_").TrimEnd('=');
                isNewKey = true;
            }

            // Set/refresh cookie on hub domain
            httpContext.Response.Cookies.Append("xcord_hub_key", key, new CookieOptions
            {
                HttpOnly = false,
                SameSite = SameSiteMode.None,
                Secure = true,
                MaxAge = TimeSpan.FromDays(365 * 5),
                Path = "/api/v1/header",
            });

            // Ensure ServerList exists
            var serverList = await db.ServerLists
                .Include(s => s.Entries)
                .FirstOrDefaultAsync(s => s.HubKey == key, ct);

            if (serverList is null)
            {
                serverList = new ServerList { HubKey = key, CreatedAt = DateTimeOffset.UtcNow };
                db.ServerLists.Add(serverList);
                await db.SaveChangesAsync(ct);
            }

            // Auto-add current serverUrl if provided and not already in list
            if (!string.IsNullOrEmpty(serverUrl))
            {
                var normalized = serverUrl.TrimEnd('/');
                var exists = serverList.Entries.Any(e =>
                    string.Equals(e.ServerUrl, normalized, StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    var (name, iconUrl) = await FetchServerInfo(normalized, ct);
                    serverList.Entries.Add(new ServerListEntry
                    {
                        HubKey = key,
                        ServerUrl = normalized,
                        ServerName = name,
                        ServerIconUrl = iconUrl,
                        AddedAt = DateTimeOffset.UtcNow,
                    });
                    await db.SaveChangesAsync(ct);
                }
            }

            var html = RenderHeader(serverList.Entries, serverUrl?.TrimEnd('/'), key, isNewKey);
            return Results.Content(html, "text/html");
        })
        .AllowAnonymous()
        .WithName("GetHeader")
        .WithTags("Header");
    }

    private static async Task<(string Name, string? IconUrl)> FetchServerInfo(
        string serverUrl, CancellationToken ct)
    {
        try
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var resp = await client.GetFromJsonAsync<InstanceInfoResponse>(
                $"{serverUrl}/api/v1/instance/info", ct);
            var name = resp?.Name ?? serverUrl;
            // Only accept HTTP(S) icon URLs
            var icon = resp?.IconUrl;
            if (!string.IsNullOrEmpty(icon) && !IsValidHttpsUrl(icon))
                icon = null;
            return (name, icon);
        }
        catch
        {
            var uri = new Uri(serverUrl);
            return (uri.Host, null);
        }
    }

    private sealed record InstanceInfoResponse(string Name, string? IconUrl);

    private static readonly Regex HubKeyPattern = new(@"^[A-Za-z0-9_\-]{1,64}$", RegexOptions.Compiled);

    private static bool IsValidHttpsUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    private static string HtmlEncode(string? value) =>
        HtmlEncoder.Default.Encode(value ?? "");

    private static string JsStringEscape(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"")
             .Replace("\n", "\\n").Replace("\r", "\\r").Replace("<", "\\x3c");

    private static string RenderHeader(
        ICollection<ServerListEntry> entries,
        string? currentServerUrl,
        string hubKey,
        bool isNewKey)
    {
        var serverButtons = string.Join("\n", entries.Select(e =>
        {
            var isCurrent = string.Equals(e.ServerUrl, currentServerUrl, StringComparison.OrdinalIgnoreCase);
            var activeClass = isCurrent ? " active" : "";
            var safeName = HtmlEncode(e.ServerName);
            var safeUrl = HtmlEncode(e.ServerUrl);
            var jsUrl = JsStringEscape(e.ServerUrl);
            var initial = string.IsNullOrEmpty(e.ServerName) ? "?" : HtmlEncode(e.ServerName[..1].ToUpper());
            var iconHtml = !string.IsNullOrEmpty(e.ServerIconUrl) && IsValidHttpsUrl(e.ServerIconUrl)
                ? $"<img src=\"{HtmlEncode(e.ServerIconUrl)}\" alt=\"{safeName}\" />"
                : $"<span class=\"initial\">{initial}</span>";
            return $"""
                <button class="server-btn{activeClass}" title="{safeName}" onclick="navigateTo('{jsUrl}')">
                    {iconHtml}
                </button>
            """;
        }));

        return $$"""
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <style>
                * { margin: 0; padding: 0; box-sizing: border-box; }
                body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background: #1a1a2e; height: 48px; overflow: hidden; }
                .header { display: flex; align-items: center; height: 48px; padding: 0 8px; gap: 4px; }
                .server-btn { width: 36px; height: 36px; border-radius: 50%; border: 2px solid transparent; background: #2a2a4a; color: #ccc; cursor: pointer; display: flex; align-items: center; justify-content: center; overflow: hidden; flex-shrink: 0; transition: border-color 0.15s, border-radius 0.15s; }
                .server-btn:hover { border-color: #5865f2; border-radius: 12px; }
                .server-btn.active { border-color: #5865f2; border-radius: 12px; }
                .server-btn img { width: 100%; height: 100%; object-fit: cover; }
                .server-btn .initial { font-size: 14px; font-weight: 600; }
                .spacer { flex: 1; }
                .add-btn { width: 36px; height: 36px; border-radius: 50%; border: 2px dashed #444; background: transparent; color: #5865f2; cursor: pointer; font-size: 20px; display: flex; align-items: center; justify-content: center; flex-shrink: 0; transition: border-color 0.15s, background 0.15s; }
                .add-btn:hover { border-color: #5865f2; background: #2a2a4a; }
                .add-input-wrap { display: none; align-items: center; gap: 4px; }
                .add-input-wrap.show { display: flex; }
                .add-input { background: #2a2a4a; border: 1px solid #444; border-radius: 6px; color: #eee; padding: 4px 8px; font-size: 13px; width: 200px; outline: none; }
                .add-input:focus { border-color: #5865f2; }
                .add-go { background: #5865f2; border: none; border-radius: 6px; color: white; padding: 4px 10px; cursor: pointer; font-size: 13px; }
            </style>
        </head>
        <body>
            <div class="header">
                {{serverButtons}}
                <div class="spacer"></div>
                <div class="add-input-wrap" id="addWrap">
                    <input class="add-input" id="addInput" type="url" placeholder="https://server.example.com" />
                    <button class="add-go" onclick="goToInput()">Go</button>
                </div>
                <button class="add-btn" id="addBtn" onclick="toggleAdd()">+</button>
            </div>
            <script>
                var hubKey = '{{JsStringEscape(hubKey)}}';
                var isNewKey = {{(isNewKey ? "true" : "false")}};
                if (isNewKey) { window.parent.postMessage({ type: 'xcord_hub_key', hubKey: hubKey }, '*'); }
                function navigateTo(url) { window.parent.location.href = url; }
                function toggleAdd() {
                    var wrap = document.getElementById('addWrap');
                    var btn = document.getElementById('addBtn');
                    var showing = wrap.classList.toggle('show');
                    btn.style.display = showing ? 'none' : 'flex';
                    if (showing) document.getElementById('addInput').focus();
                }
                function goToInput() {
                    var url = document.getElementById('addInput').value.trim();
                    if (url && !url.startsWith('http')) url = 'https://' + url;
                    if (url) window.parent.location.href = url;
                }
                document.getElementById('addInput').addEventListener('keydown', function(e) {
                    if (e.key === 'Enter') goToInput();
                    if (e.key === 'Escape') { document.getElementById('addWrap').classList.remove('show'); document.getElementById('addBtn').style.display = 'flex'; }
                });
            </script>
        </body>
        </html>
        """;
    }
}
