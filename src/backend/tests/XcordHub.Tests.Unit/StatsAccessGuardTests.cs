using System.Net;
using FluentAssertions;
using XcordHub.Features.Admin;
using Xunit;

namespace XcordHub.Tests.Unit;

/// <summary>
/// The gate on GET /api/v1/admin/stats.
///
/// This is the only admin route that accepts a machine token instead of an admin
/// JWT, and the guest's Caddyfile ends in a catch-all that proxies unclaimed
/// hostnames to the hub - so the route answers on https://xcord.net for the whole
/// internet. Each test below is one of the three things that keeps that from
/// being an open door, plus the rule that an UNSET token closes the route rather
/// than opening it.
/// </summary>
[Trait("Category", "Unit")]
public sealed class StatsAccessGuardTests
{
    private const string Token = "s3cr3t-console-token";
    private const string Tailnet = "100.64.0.3";

    private static StatsAccessGuard.Decision Evaluate(
        string? authorization,
        string? realIp = Tailnet,
        string? forwardedFor = null,
        string? expected = Token) =>
        StatsAccessGuard.Evaluate(authorization, realIp, forwardedFor, IPAddress.Loopback, expected);

    // --- the token ---------------------------------------------------------

    [Fact]
    public void NoAuthorizationHeader_IsRefused()
    {
        Evaluate(authorization: null).Should().Be(StatsAccessGuard.Decision.BadToken);
    }

    [Fact]
    public void WrongToken_IsRefused()
    {
        Evaluate($"Bearer {Token}-not").Should().Be(StatsAccessGuard.Decision.BadToken);
    }

    [Fact]
    public void RightToken_IsAllowed()
    {
        Evaluate($"Bearer {Token}").Should().Be(StatsAccessGuard.Decision.Allowed);
    }

    [Theory]
    [InlineData("bearer ")]
    [InlineData("BEARER ")]
    public void BearerScheme_IsCaseInsensitive(string scheme)
    {
        Evaluate(scheme + Token).Should().Be(StatsAccessGuard.Decision.Allowed);
    }

    [Theory]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Basic aGk6dGhlcmU=")]
    [InlineData("s3cr3t-console-token")]
    public void AnythingThatIsNotABearerToken_IsRefused(string header)
    {
        Evaluate(header).Should().Be(StatsAccessGuard.Decision.BadToken);
    }

    [Fact]
    public void TokenComparisonIsNotAPrefixMatch()
    {
        // A compare that returned early on the first differing byte would let a
        // caller walk the token out one character at a time. FixedTimeEquals over
        // SHA-256 of each side has no early exit and no length signal.
        Evaluate("Bearer s3cr3t").Should().Be(StatsAccessGuard.Decision.BadToken);
        Evaluate($"Bearer {Token}extra").Should().Be(StatsAccessGuard.Decision.BadToken);
    }

    // --- the token being unset ---------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UnsetToken_Is503_NeverAllowed(string? expected)
    {
        // Whatever the caller presents. An unset secret must never become an open
        // endpoint, and the console needs "misconfigured" to look different from
        // "unauthorized".
        Evaluate($"Bearer {Token}", expected: expected)
            .Should().Be(StatsAccessGuard.Decision.TokenNotConfigured);
        Evaluate(authorization: null, expected: expected)
            .Should().Be(StatsAccessGuard.Decision.TokenNotConfigured);
    }

    // --- the tailnet -------------------------------------------------------

    [Theory]
    [InlineData("100.64.0.1")]
    [InlineData("100.64.0.3")]
    [InlineData("100.127.255.254")]
    public void AddressesInsideTheTailnetAreAccepted(string address)
    {
        Evaluate($"Bearer {Token}", realIp: address).Should().Be(StatsAccessGuard.Decision.Allowed);
    }

    // PUBLICLY ROUTABLE hops are refused — that is the rule, and it is not the
    // rule this originally asserted.
    //
    // It required every hop to sit inside 100.64.0.0/10, which the console can
    // never satisfy: port 8080 is published by the `router` container, so every
    // request is DNAT'd by the guest's docker bridge and arrives as the bridge
    // gateway. Measured on the running guest, for a request from the spark over
    // the tailnet: {"remote_ip": "172.18.0.1", "client_ip": "172.18.0.1"}. The
    // endpoint answered 403 to the only caller it exists for.
    [Theory]
    [InlineData("203.0.113.7")]      // a public client through the ingress relay
    [InlineData("100.128.0.0")]      // just above the CGNAT range
    [InlineData("8.8.8.8")]          // plainly the internet
    [InlineData("172.32.0.1")]       // just above RFC 1918's 172.16/12
    public void PubliclyRoutableAddressesAreRefused(string address)
    {
        Evaluate($"Bearer {Token}", realIp: address).Should().Be(StatsAccessGuard.Decision.NotOnTailnet);
    }

    // Infrastructure hops the request legitimately crossed. The bridge gateway
    // is the console's real, measured path; refusing it refused the caller.
    [Theory]
    [InlineData("172.18.0.1")]       // the guest's docker bridge gateway
    [InlineData("172.18.0.4")]       // another address on that bridge
    [InlineData("127.0.0.1")]        // loopback, e.g. a probe on the guest
    [InlineData("10.1.2.3")]         // RFC 1918
    [InlineData("192.168.1.10")]     // the LAN
    public void InternalAddressesAreAllowed(string address)
    {
        Evaluate($"Bearer {Token}", realIp: address).Should().Be(StatsAccessGuard.Decision.Allowed);
    }

    // The distinguishing signal, end to end. The ingress relay records the real
    // internet client BEFORE the tailnet hop and Caddy appends to that list, so
    // a public caller still carries a public entry even though its last hop is
    // the same bridge address the console arrives on. Checking every hop is what
    // makes a forged internal-looking header useless: the relay appends the
    // caller's real address after whatever they sent.
    [Fact]
    public void APublicCallerIsRefusedEvenThoughTheLastHopIsInternal()
    {
        Evaluate($"Bearer {Token}", forwardedFor: "203.0.113.7, 172.18.0.1")
            .Should().Be(StatsAccessGuard.Decision.NotOnTailnet);

        Evaluate($"Bearer {Token}", forwardedFor: "172.18.0.1")
            .Should().Be(StatsAccessGuard.Decision.Allowed);
    }

    [Fact]
    public void TheRefusalOutranksTheToken()
    {
        // Checked before the token, so a public caller cannot use this route to
        // find out whether a token is configured, nor spend the rate limit.
        Evaluate("Bearer wrong", realIp: "203.0.113.7")
            .Should().Be(StatsAccessGuard.Decision.NotOnTailnet);
        Evaluate($"Bearer {Token}", realIp: "203.0.113.7", expected: null)
            .Should().Be(StatsAccessGuard.Decision.NotOnTailnet);
    }

    [Fact]
    public void ASpoofedForwardedForCannotHideAPublicAddress()
    {
        // The relay's X-Forwarded-For is $proxy_add_x_forwarded_for, which
        // APPENDS the peer it saw: a caller who sends "100.64.0.9" arrives as
        // "100.64.0.9, 203.0.113.7". Requiring EVERY hop to be on the tailnet is
        // what makes the leftmost entry - the only one the caller controls -
        // useless to them.
        StatsAccessGuard.Evaluate(
            $"Bearer {Token}",
            realIp: null,
            forwardedFor: "100.64.0.9, 203.0.113.7",
            remoteAddress: IPAddress.Loopback,
            expectedToken: Token)
            .Should().Be(StatsAccessGuard.Decision.NotOnTailnet);
    }

    [Fact]
    public void XRealIpIsBelievedOverAForgedForwardedFor()
    {
        // nginx sets X-Real-IP to $remote_addr, overwriting whatever the client
        // sent, so it is the one header a public caller cannot forge.
        StatsAccessGuard.Evaluate(
            $"Bearer {Token}",
            realIp: "203.0.113.7",
            forwardedFor: "100.64.0.3",
            remoteAddress: IPAddress.Loopback,
            expectedToken: Token)
            .Should().Be(StatsAccessGuard.Decision.NotOnTailnet);
    }

    [Fact]
    public void AProxyChainEntirelyOnTheTailnetIsAccepted()
    {
        StatsAccessGuard.Evaluate(
            $"Bearer {Token}",
            realIp: null,
            forwardedFor: "100.64.0.3, 100.64.0.5",
            remoteAddress: IPAddress.Loopback,
            expectedToken: Token)
            .Should().Be(StatsAccessGuard.Decision.Allowed);
    }

    [Fact]
    public void AnUnreadableForwardedForVouchesForNobody()
    {
        StatsAccessGuard.Evaluate(
            $"Bearer {Token}",
            realIp: null,
            forwardedFor: "unknown",
            remoteAddress: IPAddress.Parse(Tailnet),
            expectedToken: Token)
            .Should().Be(StatsAccessGuard.Decision.NotOnTailnet);
    }

    [Fact]
    public void WithNoForwardedHeadersTheSocketPeerIsUsed()
    {
        // Reaching the hub straight off the compose bridge is what the console's
        // request actually looks like once the guest's docker NAT has rewritten
        // it, so it is allowed; a public peer is not.
        StatsAccessGuard.Evaluate($"Bearer {Token}", null, null, IPAddress.Parse("172.18.0.4"), Token)
            .Should().Be(StatsAccessGuard.Decision.Allowed);
        StatsAccessGuard.Evaluate($"Bearer {Token}", null, null, IPAddress.Parse(Tailnet), Token)
            .Should().Be(StatsAccessGuard.Decision.Allowed);
        StatsAccessGuard.Evaluate($"Bearer {Token}", null, null, IPAddress.Parse("203.0.113.7"), Token)
            .Should().Be(StatsAccessGuard.Decision.NotOnTailnet);
    }

    [Fact]
    public void AnIPv4MappedAddressIsTheSameAddress()
    {
        // Kestrel reports IPv4 peers in mapped form on a dual-stack socket.
        StatsAccessGuard.Evaluate($"Bearer {Token}", "::ffff:100.64.0.3", null, null, Token)
            .Should().Be(StatsAccessGuard.Decision.Allowed);
    }

    [Fact]
    public void AForwardedEntryMayCarryAPort()
    {
        StatsAccessGuard.Evaluate($"Bearer {Token}", "100.64.0.3:41234", null, null, Token)
            .Should().Be(StatsAccessGuard.Decision.Allowed);
    }

    [Fact]
    public void NothingToCheckIsNotTheSameAsBeingOnTheTailnet()
    {
        StatsAccessGuard.Evaluate($"Bearer {Token}", null, null, null, Token)
            .Should().Be(StatsAccessGuard.Decision.NotOnTailnet);
    }
}
