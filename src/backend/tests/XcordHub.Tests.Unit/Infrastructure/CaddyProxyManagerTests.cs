using XcordHub.Infrastructure.Services;

namespace XcordHub.Tests.Unit.Infrastructure;

public sealed class CaddyProxyManagerTests
{
    [Fact]
    public async Task NoopCaddyProxyManager_CreateRouteAsync_ReturnsRouteId()
    {
        // Arrange
        var manager = new NoopCaddyProxyManager();
        var domain = "test.xcord.net";
        var containerName = "xcord-test-api";

        // Act
        var result = await manager.CreateRouteAsync(domain, containerName);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("route_", result);
    }

    [Fact]
    public async Task NoopCaddyProxyManager_VerifyRouteAsync_ReturnsTrue()
    {
        // Arrange
        var manager = new NoopCaddyProxyManager();

        // Act
        var result = await manager.VerifyRouteAsync("route_123");

        // Assert
        Assert.True(result);
    }
}
