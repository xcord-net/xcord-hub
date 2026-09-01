using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace XcordHub.Features.Admin;

/// <summary>
/// The gate on GET /api/v1/admin/stats.
///
/// Every other admin route is behind <see cref="Policies.Admin"/> - an RS256 JWT
/// carrying admin=true, issued to a human who logged in. The spark console is not
/// a human and has no session, so this one route also accepts a bearer token.
/// That is a second door into the admin surface, so it is narrower than the first
/// in three independent ways, and all three must pass:
///
///   1. The token is compared in CONSTANT TIME. A byte-by-byte compare that
///      returns early leaks the token one character at a time to anyone who can
///      measure the response.
///   2. The route is RATE LIMITED per client address, so the remaining brute
///      force is not free.
///   3. The forwarded client address must be inside the tailnet. The guest's
///      Caddyfile ends in `handle { reverse_proxy hub:80 }`, so this path
///      answers on https://xcord.net/api/v1/admin/stats for the whole internet.
///
/// If the token is not configured the route answers 503, never 200: an unset
/// secret must not become an open endpoint.
/// </summary>
public static class StatsAccessGuard
{
    /// <summary>
    /// The configuration key holding the machine token. It is read through
    /// IConfiguration rather than Environment.GetEnvironmentVariable so tests can
    /// supply it without mutating process-global state; the default host builder
    /// reads plain environment variables into configuration under this same name.
    /// </summary>
    public const string TokenConfigurationKey = "XCORD_STATS_TOKEN";

    /// <summary>
    /// The route this guard protects. The rate limiter matches on it directly
    /// rather than through a named policy, because Program.cs calls
    /// UseRateLimiter BEFORE UseRouting - at which point no endpoint has been
    /// selected, so RequireRateLimiting metadata is never read and every named
    /// policy in this application is inert. Reordering that middleware would fix
    /// this route and simultaneously switch on five others, three of which share
    /// ONE global 3-per-minute bucket across all callers and would take
    /// registration down for the whole site. That is its own change with its own
    /// blast radius; this route does not need to wait for it.
    /// </summary>
    public const string RoutePath = "/api/v1/admin/stats";

    // 100.64.0.0/10 - the CGNAT range tailscale allocates from. The guest's Caddy
    // sets `trusted_proxies static 100.64.0.0/10`, which is what makes the
    // forwarded headers below evidence rather than caller-supplied strings.
    private const int TailnetPrefixLength = 10;
    private static readonly uint TailnetNetwork = ToUInt32(IPAddress.Parse("100.64.0.0"));
    private static readonly uint TailnetMask = uint.MaxValue << (32 - TailnetPrefixLength);

    public enum Decision
    {
        /// <summary>Token matched and the caller is on the tailnet.</summary>
        Allowed,

        /// <summary>XCORD_STATS_TOKEN is unset - answer 503, never 200.</summary>
        TokenNotConfigured,

        /// <summary>Missing or wrong bearer token - answer 401.</summary>
        BadToken,

        /// <summary>Reached us from off the tailnet - answer 403.</summary>
        NotOnTailnet
    }

    /// <summary>True when this request is for the stats route.</summary>
    public static bool IsStatsRoute(HttpContext context) =>
        context.Request.Path.StartsWithSegments(RoutePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The rate-limiter partition for a stats request: the forwarded caller, not
    /// the socket peer. Behind the guest's router container every request shares
    /// one RemoteIpAddress, so partitioning on that would put the whole internet
    /// in a single bucket together with the console - and the console's own
    /// polling would be what exhausted it.
    /// </summary>
    public static string RateLimitKey(HttpContext context)
    {
        var forwarded = ForwardedAddresses(
            context.Request.Headers["X-Real-IP"],
            context.Request.Headers["X-Forwarded-For"],
            context.Connection.RemoteIpAddress);

        return forwarded.Count > 0 ? $"admin-stats:{forwarded[0]}" : "admin-stats:unknown";
    }

    /// <summary>Evaluates a live request.</summary>
    public static Decision Evaluate(HttpContext context, string? expectedToken) =>
        Evaluate(
            context.Request.Headers.Authorization,
            context.Request.Headers["X-Real-IP"],
            context.Request.Headers["X-Forwarded-For"],
            context.Connection.RemoteIpAddress,
            expectedToken);

    /// <summary>
    /// The decision itself, over the four inputs it actually depends on, so the
    /// rules are testable without a request.
    ///
    /// The tailnet check runs FIRST: a caller from the public internet should not
    /// be able to probe whether the token is configured, and should not spend the
    /// rate limiter's budget on guesses that could never have been answered.
    /// </summary>
    public static Decision Evaluate(
        string? authorizationHeader,
        string? realIp,
        string? forwardedFor,
        IPAddress? remoteAddress,
        string? expectedToken)
    {
        if (!IsTailnetClient(ForwardedAddresses(realIp, forwardedFor, remoteAddress)))
            return Decision.NotOnTailnet;

        if (string.IsNullOrWhiteSpace(expectedToken))
            return Decision.TokenNotConfigured;

        return TokenMatches(authorizationHeader, expectedToken)
            ? Decision.Allowed
            : Decision.BadToken;
    }

    /// <summary>
    /// Constant-time comparison of the request's bearer token against the
    /// configured one. Both sides are hashed first so the comparison is constant
    /// time in the LENGTH as well as the content - FixedTimeEquals alone returns
    /// immediately when the lengths differ, which tells an attacker how long the
    /// token is.
    /// </summary>
    public static bool TokenMatches(string? authorizationHeader, string? expectedToken)
    {
        if (string.IsNullOrWhiteSpace(expectedToken))
            return false;

        var presented = ExtractBearerToken(authorizationHeader);
        if (presented is null)
            return false;

        Span<byte> presentedHash = stackalloc byte[32];
        Span<byte> expectedHash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(presented), presentedHash);
        SHA256.HashData(Encoding.UTF8.GetBytes(expectedToken), expectedHash);

        return CryptographicOperations.FixedTimeEquals(presentedHash, expectedHash);
    }

    /// <summary>
    /// The addresses a request claims to have come from, most trustworthy first.
    ///
    /// The ingress relay sets X-Real-IP to the TCP peer it saw ($remote_addr), so
    /// a client cannot forge it. X-Forwarded-For is $proxy_add_x_forwarded_for,
    /// which APPENDS: its leftmost entry is whatever the caller sent and its
    /// rightmost is a proxy, so neither end of it can be trusted on its own. What
    /// IS true is that a public caller's own address is always somewhere in that
    /// list - so every entry has to be on the tailnet for the request to pass.
    ///
    /// With no forwarded headers at all we fall back to the socket peer, which
    /// inside the compose network is the router container and is therefore
    /// refused. That is deliberate: reaching the hub directly from the bridge
    /// bypasses the proxy that makes any of this checkable.
    /// </summary>
    public static IReadOnlyList<IPAddress> ForwardedAddresses(
        string? realIp,
        string? forwardedFor,
        IPAddress? remoteAddress)
    {
        var addresses = new List<IPAddress>();

        if (!string.IsNullOrWhiteSpace(realIp) && TryParse(realIp, out var real))
            addresses.Add(real);

        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            foreach (var hop in forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                // An unparseable hop is not ignored - a header we cannot read is
                // not a header that vouches for the caller.
                if (!TryParse(hop, out var parsed))
                    return [];
                addresses.Add(parsed);
            }
        }

        if (addresses.Count == 0 && remoteAddress is not null)
            addresses.Add(remoteAddress);

        return addresses;
    }

    /// <summary>
    /// True when no address the request was forwarded through is publicly
    /// routable.
    ///
    /// This required every hop to sit inside 100.64.0.0/10, which cannot ever be
    /// satisfied here — it refused the only caller it was written to admit.
    /// Measured against the running guest, for a request from the spark over the
    /// tailnet:
    ///
    ///   router  {"remote_ip": "172.18.0.1", "client_ip": "172.18.0.1"}
    ///
    /// Port 8080 is published by the `router` container, so every request — from
    /// the tailnet, from the ingress relay, from anywhere — is DNAT'd by the
    /// guest's docker bridge and reaches Caddy as the bridge gateway. The
    /// tailnet source address does not survive that hop and cannot be recovered
    /// from inside the guest.
    ///
    /// What DOES survive is the ingress relay's own X-Forwarded-For: nginx on
    /// the public host records the real internet client before the tailnet hop,
    /// and Caddy appends to that list rather than replacing it. So the two cases
    /// remain distinguishable, just not by the test that was written:
    ///
    ///   console over the tailnet  ->  "172.18.0.1"
    ///   someone on the internet   ->  "203.0.113.7, 172.18.0.1"
    ///
    /// Hence: reject if ANY hop is publicly routable. Private, loopback, CGNAT
    /// and link-local hops are infrastructure this request legitimately crossed;
    /// a public one is a person, and a person did not come from the console.
    /// Checking every hop rather than the first also means a caller who forges
    /// an internal-looking X-Forwarded-For cannot help themselves — the relay
    /// appends their real address after it.
    /// </summary>
    public static bool IsTailnetClient(IReadOnlyList<IPAddress> addresses) =>
        addresses.Count > 0 && !addresses.Any(IsPubliclyRoutable);

    private static bool IsPubliclyRoutable(IPAddress address)
    {
        // Kestrel reports IPv4 peers as ::ffff:172.18.0.1 when the socket is
        // dual-stack; the mapped form is the same address.
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return false;

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            // IPv6: unique-local (fc00::/7) and link-local are internal; the
            // rest of the space is not. Nothing on this path speaks IPv6 today,
            // so the conservative answer is also the safe one.
            return !address.IsIPv6LinkLocal
                   && !address.IsIPv6SiteLocal
                   && (address.GetAddressBytes()[0] & 0xFE) != 0xFC;
        }

        var octets = address.GetAddressBytes();

        // RFC 1918 — 10/8, 172.16/12, 192.168/16. The docker bridge lives in the
        // second of these, which is the whole reason this method exists.
        if (octets[0] == 10) return false;
        if (octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31) return false;
        if (octets[0] == 192 && octets[1] == 168) return false;

        // 169.254/16 link-local.
        if (octets[0] == 169 && octets[1] == 254) return false;

        // 100.64/10 — the CGNAT range tailscale allocates from.
        if ((ToUInt32(address) & TailnetMask) == TailnetNetwork) return false;

        return true;
    }

    private static bool TryParse(string candidate, out IPAddress address)
    {
        candidate = candidate.Trim();

        // XFF entries may carry a port ("100.64.0.3:41234"); IPv6 forms arrive
        // bracketed. IPAddress.TryParse rejects both, so strip them first.
        if (candidate.StartsWith('['))
        {
            var close = candidate.IndexOf(']');
            if (close > 0)
                candidate = candidate[1..close];
        }
        else if (candidate.Count(c => c == ':') == 1)
        {
            candidate = candidate[..candidate.IndexOf(':')];
        }

        return IPAddress.TryParse(candidate, out address!);
    }

    private static string? ExtractBearerToken(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
            return null;

        const string prefix = "Bearer ";
        if (!authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var token = authorizationHeader[prefix.Length..].Trim();
        return token.Length == 0 ? null : token;
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }
}
