using XcordHub.Infrastructure.Services;

namespace XcordHub.Tests.Unit.Infrastructure;

public sealed class DnsProviderTests
{
    [Fact]
    public async Task NoopDnsProvider_VerifyDnsRecordAsync_ReturnsTrue()
    {
        // Arrange
        var provider = new NoopDnsProvider();

        // Act
        var result = await provider.VerifyDnsRecordAsync("test");

        // Assert
        Assert.True(result);
    }
}
