using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Tests.Unit.Infrastructure;

/// <summary>
/// Unit tests for HttpInstanceNotifier.
/// Verifies that notifications are sent with the correct URL and payload, and that
/// all failure modes (network error, timeout, non-2xx response) are silently absorbed
/// so that suspension proceeds even when the instance is unreachable.
/// </summary>
public sealed class InstanceNotifierTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Creates an HttpInstanceNotifier backed by a handler that always returns the
    /// supplied response.
    /// </summary>
    private static (HttpInstanceNotifier notifier, FakeHttpMessageHandler handler) CreateNotifier(
        HttpResponseMessage response)
    {
        var fakeHandler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(fakeHandler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        var logger = NullLogger<HttpInstanceNotifier>.Instance;
        var notifier = new HttpInstanceNotifier(httpClient, logger);
        return (notifier, fakeHandler);
    }

    /// <summary>
    /// Creates an HttpInstanceNotifier backed by a handler that always throws the
    /// supplied exception (simulates network failure or timeout).
    /// </summary>
    private static HttpInstanceNotifier CreateThrowingNotifier(Exception ex)
    {
        var fakeHandler = new ThrowingHttpMessageHandler(ex);
        var httpClient = new HttpClient(fakeHandler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        var logger = NullLogger<HttpInstanceNotifier>.Instance;
        return new HttpInstanceNotifier(httpClient, logger);
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task NotifyShuttingDownAsync_Success_SendsPostToCorrectUrl()
    {
        // Arrange
        var (notifier, handler) = CreateNotifier(new HttpResponseMessage(HttpStatusCode.OK));

        // Act — must not throw
        await notifier.NotifyShuttingDownAsync("alpha.xcord.net", "suspended by hub");

        // Assert — exactly one request was sent to the expected container-internal URL
        handler.Requests.Should().HaveCount(1);
        var request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Post);
        // HttpClient normalises http:// URIs by omitting the default port 80;
        // the notifier builds "http://xcord-alpha-api:80/..." but the Uri round-trips as
        // "http://xcord-alpha-api/..." — assert on the effective URL.
        request.RequestUri!.ToString().Should().Be("http://xcord-alpha-api/api/v1/internal/shutdown");
    }

    [Fact]
    public async Task NotifyShuttingDownAsync_Success_IncludesReasonInBody()
    {
        // Arrange
        var (notifier, handler) = CreateNotifier(new HttpResponseMessage(HttpStatusCode.OK));
        const string reason = "billing";

        // Act
        await notifier.NotifyShuttingDownAsync("beta.xcord.net", reason);

        // Assert — request body contains the reason
        handler.Requests.Should().HaveCount(1);
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        body.Should().Contain(reason, "the reason must be included in the JSON payload");
    }

    [Fact]
    public async Task NotifyShuttingDownAsync_NonSuccessStatus_DoesNotThrow()
    {
        // Arrange — server returns 503
        var (notifier, _) = CreateNotifier(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        // Act & Assert — failure must be absorbed; suspension should proceed normally
        await notifier.Invoking(n => n.NotifyShuttingDownAsync("gamma.xcord.net", "admin action"))
            .Should().NotThrowAsync("non-2xx responses must not propagate");
    }

    [Fact]
    public async Task NotifyShuttingDownAsync_NetworkException_DoesNotThrow()
    {
        // Arrange — simulates an instance that is already stopped (connection refused)
        var notifier = CreateThrowingNotifier(new HttpRequestException("Connection refused"));

        // Act & Assert
        await notifier.Invoking(n => n.NotifyShuttingDownAsync("delta.xcord.net", "suspended by hub"))
            .Should().NotThrowAsync("network errors must be absorbed so suspension proceeds");
    }

    [Fact]
    public async Task NotifyShuttingDownAsync_TimeoutException_DoesNotThrow()
    {
        // Arrange — simulates a slow/unresponsive instance
        var notifier = CreateThrowingNotifier(new TaskCanceledException("Request timed out"));

        // Act & Assert
        await notifier.Invoking(n => n.NotifyShuttingDownAsync("epsilon.xcord.net", "suspended by hub"))
            .Should().NotThrowAsync("timeouts must be absorbed so suspension proceeds");
    }

    [Fact]
    public async Task NotifyShuttingDownAsync_DerivesContainerHostFromSubdomain()
    {
        // Arrange — domain with multi-segment public suffix; only the first label is used
        var (notifier, handler) = CreateNotifier(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        await notifier.NotifyShuttingDownAsync("myserver.xcord.net", "suspended by hub");

        // Assert — container host is derived from just the subdomain label
        handler.Requests.Should().HaveCount(1);
        var uri = handler.Requests[0].RequestUri!;
        uri.Host.Should().Be("xcord-myserver-api");
    }

    // ---------------------------------------------------------------------------
    // Test doubles
    // ---------------------------------------------------------------------------

    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHttpMessageHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
    }
}
