using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading.RateLimiting;

namespace XcordHub.Tests.Security;

[Trait("Category", "Security")]
public sealed class RateLimitingTests
{
    [Fact]
    public async Task RateLimit_ShouldAllow_RequestsWithinLimit()
    {
        // Arrange
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRateLimiter(options =>
                        {
                            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                            {
                                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                                return RateLimitPartition.GetTokenBucketLimiter(ipAddress, _ => new TokenBucketRateLimiterOptions
                                {
                                    TokenLimit = 10,
                                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                                    TokensPerPeriod = 10,
                                    AutoReplenishment = true
                                });
                            });
                        });
                    })
                    .Configure(app =>
                    {
                        app.UseRateLimiter();
                        app.Run(async context =>
                        {
                            context.Response.StatusCode = 200;
                            await context.Response.WriteAsync("OK");
                        });
                    });
            })
            .StartAsync();

        var client = host.GetTestServer().CreateClient();

        // Act - make 5 requests within limit
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 5; i++)
        {
            responses.Add(await client.GetAsync("/"));
        }

        // Assert
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
    }

    [Fact]
    public async Task RateLimit_ShouldBlock_RequestsExceedingLimit()
    {
        // Arrange
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRateLimiter(options =>
                        {
                            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                            {
                                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                                return RateLimitPartition.GetTokenBucketLimiter(ipAddress, _ => new TokenBucketRateLimiterOptions
                                {
                                    TokenLimit = 5,
                                    ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                                    TokensPerPeriod = 1,
                                    AutoReplenishment = true
                                });
                            });
                        });
                    })
                    .Configure(app =>
                    {
                        app.UseRateLimiter();
                        app.Run(async context =>
                        {
                            context.Response.StatusCode = 200;
                            await context.Response.WriteAsync("OK");
                        });
                    });
            })
            .StartAsync();

        var client = host.GetTestServer().CreateClient();

        // Act - make requests exceeding the limit
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 10; i++)
        {
            responses.Add(await client.GetAsync("/"));
        }

        // Assert
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var rateLimitedCount = responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests);

        successCount.Should().BeLessThanOrEqualTo(5);
        rateLimitedCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RateLimit_ShouldReturn429_WhenExceeded()
    {
        // Arrange
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRateLimiter(options =>
                        {
                            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                            {
                                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                                return RateLimitPartition.GetTokenBucketLimiter(ipAddress, _ => new TokenBucketRateLimiterOptions
                                {
                                    TokenLimit = 2,
                                    ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                                    TokensPerPeriod = 1,
                                    AutoReplenishment = true
                                });
                            });
                        });
                    })
                    .Configure(app =>
                    {
                        app.UseRateLimiter();
                        app.Run(async context =>
                        {
                            context.Response.StatusCode = 200;
                            await context.Response.WriteAsync("OK");
                        });
                    });
            })
            .StartAsync();

        var client = host.GetTestServer().CreateClient();

        // Act - exhaust the limit
        await client.GetAsync("/");
        await client.GetAsync("/");
        var blockedResponse = await client.GetAsync("/");

        // Assert
        blockedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
