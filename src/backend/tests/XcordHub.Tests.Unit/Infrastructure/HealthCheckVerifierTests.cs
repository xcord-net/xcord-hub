using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Tests.Unit.Infrastructure;

/// <summary>
/// Unit tests for HttpHealthCheckVerifier.
/// Verifies that health check URLs use the public domain rather than Docker container
/// names, so the verifier works both inside and outside Docker networks.
/// </summary>
public sealed class HealthCheckVerifierTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static readonly IHostEnvironment ProductionEnv = new FakeHostEnvironment("Production");
    private static readonly IHostEnvironment DevelopmentEnv = new FakeHostEnvironment("Development");


    private static (HttpHealthCheckVerifier verifier, FakeHttpMessageHandler handler) CreateVerifier(
        HttpResponseMessage response, IHostEnvironment? env = null)
    {
        var handler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        var logger = NullLogger<HttpHealthCheckVerifier>.Instance;
        var verifier = new HttpHealthCheckVerifier(httpClient, env ?? ProductionEnv, logger);
        return (verifier, handler);
    }

    private static HttpHealthCheckVerifier CreateThrowingVerifier(Exception ex, IHostEnvironment? env = null)
    {
        var handler = new ThrowingHttpMessageHandler(ex);
        var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        var logger = NullLogger<HttpHealthCheckVerifier>.Instance;
        return new HttpHealthCheckVerifier(httpClient, env ?? ProductionEnv, logger);
    }

    private static HttpResponseMessage OkWithVersion(string version)
    {
        var body = JsonSerializer.Serialize(new { version });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }

    // ---------------------------------------------------------------------------
    // URL construction
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task VerifyInstanceHealthAsync_UsesPublicDomainNotContainerName()
    {
        // Arrange
        var (verifier, handler) = CreateVerifier(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        await verifier.VerifyInstanceHealthAsync("tserver.xcord-dev.net");

        // Assert - URL must use the public domain, never a Docker container name
        handler.Requests.Should().HaveCount(1);
        var uri = handler.Requests[0].RequestUri!;
        uri.Host.Should().Be("tserver.xcord-dev.net",
            "health checks must use the public domain so they work both inside and outside Docker");
        uri.Host.Should().NotContain("xcord-tserver-api",
            "Docker container names are not resolvable outside Docker networks");
    }

    [Fact]
    public async Task VerifyInstanceHealthAsync_Production_UsesHttpsScheme()
    {
        // Arrange
        var (verifier, handler) = CreateVerifier(new HttpResponseMessage(HttpStatusCode.OK), ProductionEnv);

        // Act
        await verifier.VerifyInstanceHealthAsync("tserver.xcord-dev.net");

        // Assert - production uses HTTPS via Caddy TLS
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.Scheme.Should().Be("https");
    }

    [Fact]
    public async Task VerifyInstanceHealthAsync_Development_UsesHttpScheme()
    {
        // Arrange
        var (verifier, handler) = CreateVerifier(new HttpResponseMessage(HttpStatusCode.OK), DevelopmentEnv);

        // Act
        await verifier.VerifyInstanceHealthAsync("tserver.xcord-dev.net");

        // Assert - dev uses HTTP (no TLS on local fed instances)
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.Scheme.Should().Be("http");
    }

    [Fact]
    public async Task VerifyInstanceHealthAsync_TargetsCorrectPath()
    {
        // Arrange
        var (verifier, handler) = CreateVerifier(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        await verifier.VerifyInstanceHealthAsync("myserver.xcord.net");

        // Assert
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/v1/health");
    }

    [Fact]
    public async Task VerifyInstanceHealthAsync_Production_FullUrlIsCorrect()
    {
        // Arrange
        var (verifier, handler) = CreateVerifier(new HttpResponseMessage(HttpStatusCode.OK), ProductionEnv);

        // Act
        await verifier.VerifyInstanceHealthAsync("alpha.xcord-dev.net");

        // Assert - the complete URL uses HTTPS + public domain + health path
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://alpha.xcord-dev.net/api/v1/health");
    }

    // ---------------------------------------------------------------------------
    // 200 OK - healthy result
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task VerifyInstanceHealthAsync_200Ok_ReturnsHealthy()
    {
        // Arrange
        var (verifier, _) = CreateVerifier(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var (isHealthy, _, errorMessage, _) = await verifier.VerifyInstanceHealthAsync("alpha.xcord-dev.net");

        // Assert
        isHealthy.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public async Task VerifyInstanceHealthAsync_200OkWithVersion_ParsesVersion()
    {
        // Arrange
        var (verifier, _) = CreateVerifier(OkWithVersion("1.2.3"));

        // Act
        var (isHealthy, _, _, version) = await verifier.VerifyInstanceHealthAsync("alpha.xcord-dev.net");

        // Assert
        isHealthy.Should().BeTrue();
        version.Should().Be("1.2.3");
    }

    [Fact]
    public async Task VerifyInstanceHealthAsync_200Ok_RecordsPositiveResponseTime()
    {
        // Arrange
        var (verifier, _) = CreateVerifier(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var (_, responseTimeMs, _, _) = await verifier.VerifyInstanceHealthAsync("alpha.xcord-dev.net");

        // Assert - response time must be non-negative
        responseTimeMs.Should().BeGreaterThanOrEqualTo(0);
    }

    // ---------------------------------------------------------------------------
    // Non-200 responses - unhealthy result
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task VerifyInstanceHealthAsync_NonSuccessStatus_ReturnsUnhealthy(HttpStatusCode statusCode)
    {
        // Arrange
        var (verifier, _) = CreateVerifier(new HttpResponseMessage(statusCode));

        // Act
        var (isHealthy, _, errorMessage, _) = await verifier.VerifyInstanceHealthAsync("alpha.xcord-dev.net");

        // Assert
        isHealthy.Should().BeFalse();
        errorMessage.Should().NotBeNullOrEmpty();
        errorMessage.Should().Contain(((int)statusCode).ToString());
    }

    // ---------------------------------------------------------------------------
    // Network failures - caught and returned as unhealthy, not thrown
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task VerifyInstanceHealthAsync_NetworkException_ReturnsUnhealthyWithoutThrowing()
    {
        // Arrange
        var verifier = CreateThrowingVerifier(new HttpRequestException("Connection refused"));

        // Act
        var act = async () => await verifier.VerifyInstanceHealthAsync("alpha.xcord-dev.net");

        // Assert - must not propagate
        await act.Should().NotThrowAsync();
        var (isHealthy, _, errorMessage, _) = await verifier.VerifyInstanceHealthAsync("alpha.xcord-dev.net");
        isHealthy.Should().BeFalse();
        errorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task VerifyInstanceHealthAsync_DnsResolutionFailure_ReturnsUnhealthyWithoutThrowing()
    {
        // Arrange - simulates what used to happen with Docker container names outside Docker
        var verifier = CreateThrowingVerifier(
            new HttpRequestException("Name or service not known (xcord-tserver-api:80)"));

        // Act & Assert
        await verifier.Invoking(v => v.VerifyInstanceHealthAsync("tserver.xcord-dev.net"))
            .Should().NotThrowAsync("DNS failures must be absorbed as unhealthy, not exceptions");
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
