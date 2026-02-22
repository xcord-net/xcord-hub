using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using XcordHub.Api;

namespace XcordHub.Tests.Security;

[Trait("Category", "Security")]
public sealed class SecurityHeaderTests
{
    [Fact]
    public async Task SecurityHeaders_ShouldBePresent()
    {
        // Arrange
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseSecurityHeaders();
                        app.Run(async context =>
                        {
                            context.Response.StatusCode = 200;
                            await context.Response.WriteAsync("OK");
                        });
                    });
            })
            .StartAsync();

        var client = host.GetTestServer().CreateClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert
        response.Should().NotBeNull();
        response.Headers.Should().ContainKey("X-Content-Type-Options");
        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");

        response.Headers.Should().ContainKey("X-Frame-Options");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");

        response.Headers.Should().ContainKey("Content-Security-Policy");
        response.Headers.GetValues("Content-Security-Policy").Should().ContainMatch("*script-src 'self'*");
        response.Headers.GetValues("Content-Security-Policy").Should().ContainMatch("*frame-ancestors 'none'*");

        response.Headers.Should().ContainKey("X-XSS-Protection");
        response.Headers.GetValues("X-XSS-Protection").Should().Contain("0");

        response.Headers.Should().ContainKey("Referrer-Policy");
        response.Headers.GetValues("Referrer-Policy").Should().Contain("strict-origin-when-cross-origin");

        response.Headers.Should().ContainKey("Permissions-Policy");
        response.Headers.GetValues("Permissions-Policy").Should().Contain("camera=(), microphone=(), geolocation=()");

        response.Headers.Should().ContainKey("Strict-Transport-Security");
        response.Headers.GetValues("Strict-Transport-Security").Should().Contain("max-age=31536000; includeSubDomains");
    }

    [Fact]
    public async Task Hsts_ShouldNotBePresent_InDevelopment()
    {
        // Arrange
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .UseEnvironment("Development")
                    .Configure(app =>
                    {
                        app.UseSecurityHeaders();
                        app.Run(async context =>
                        {
                            context.Response.StatusCode = 200;
                            await context.Response.WriteAsync("OK");
                        });
                    });
            })
            .StartAsync();

        var client = host.GetTestServer().CreateClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert
        response.Headers.Should().NotContainKey("Strict-Transport-Security");
    }

    [Fact]
    public async Task ContentSecurityPolicy_ShouldPreventFraming()
    {
        // Arrange
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseSecurityHeaders();
                        app.Run(async context =>
                        {
                            context.Response.StatusCode = 200;
                            await context.Response.WriteAsync("OK");
                        });
                    });
            })
            .StartAsync();

        var client = host.GetTestServer().CreateClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert
        var cspHeader = response.Headers.GetValues("Content-Security-Policy").FirstOrDefault();
        cspHeader.Should().Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task XContentTypeOptions_ShouldPreventMimeSniffing()
    {
        // Arrange
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseSecurityHeaders();
                        app.Run(async context =>
                        {
                            context.Response.StatusCode = 200;
                            await context.Response.WriteAsync("OK");
                        });
                    });
            })
            .StartAsync();

        var client = host.GetTestServer().CreateClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert
        var header = response.Headers.GetValues("X-Content-Type-Options").FirstOrDefault();
        header.Should().Be("nosniff");
    }
}
