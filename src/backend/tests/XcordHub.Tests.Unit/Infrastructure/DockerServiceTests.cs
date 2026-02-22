using Microsoft.Extensions.Logging;
using NSubstitute;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Tests.Unit.Infrastructure;

public sealed class DockerServiceTests
{
    [Fact]
    public async Task NoopDockerService_CreateNetworkAsync_ReturnsNetworkId()
    {
        // Arrange
        var logger = Substitute.For<ILogger<NoopDockerService>>();
        var service = new NoopDockerService(logger);
        var domain = "test.xcord.net";

        // Act
        var result = await service.CreateNetworkAsync(domain);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("network_", result);
    }

    [Fact]
    public async Task NoopDockerService_VerifyNetworkAsync_ReturnsTrue()
    {
        // Arrange
        var logger = Substitute.For<ILogger<NoopDockerService>>();
        var service = new NoopDockerService(logger);

        // Act
        var result = await service.VerifyNetworkAsync("network_123");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task NoopDockerService_StartContainerAsync_ReturnsContainerId()
    {
        // Arrange
        var logger = Substitute.For<ILogger<NoopDockerService>>();
        var service = new NoopDockerService(logger);
        var domain = "test.xcord.net";
        var config = "{}";

        // Act
        var result = await service.StartContainerAsync(domain, config);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("container_", result);
    }

    [Fact]
    public async Task NoopDockerService_VerifyContainerRunningAsync_ReturnsTrue()
    {
        // Arrange
        var logger = Substitute.For<ILogger<NoopDockerService>>();
        var service = new NoopDockerService(logger);

        // Act
        var result = await service.VerifyContainerRunningAsync("container_123");

        // Assert
        Assert.True(result);
    }
}
