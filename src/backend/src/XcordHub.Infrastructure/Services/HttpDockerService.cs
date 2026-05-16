using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IDockerService"/> by talking to the Docker socket via
/// the docker-socket-proxy HTTP client. Split across multiple partial files by
/// Docker API concern:
///   - HttpDockerService.Networks.cs (overlay networks)
///   - HttpDockerService.Secrets.cs  (config + KEK secrets)
///   - HttpDockerService.Services.cs (Swarm service create/start/stop/remove/migrate)
/// This main file holds the constructor, shared fields, and private DTOs.
/// </summary>
public sealed partial class HttpDockerService : IDockerService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpDockerService> _logger;
    private readonly string _instanceImage;
    private readonly string _instanceEnvironment;

    // xcord-shared-net: dev/single-host network shared by all compose services.
    // In production, each compute pool uses its own overlay network (xcord-pool-{name})
    // so instances on different pools cannot reach each other directly.
    // xcord-hub-infra-net (where docker-socket-proxy lives) is never attached to
    // instance containers - preventing a compromised instance from reaching the Docker API.
    private const string SharedNetworkName = "xcord-shared-net";

    public HttpDockerService(IHttpClientFactory httpClientFactory, ILogger<HttpDockerService> logger, IOptions<DockerOptions> options, IHostEnvironment hostEnvironment)
    {
        _httpClient = httpClientFactory.CreateClient("DockerSocketProxy");
        _logger = logger;
        var opts = options.Value;
        _instanceImage = string.IsNullOrWhiteSpace(opts.InstanceImage)
            ? "xcord-fed:latest"
            : opts.InstanceImage;
        _instanceEnvironment = hostEnvironment.EnvironmentName;
    }

    // ---------------------------------------------------------------------
    // Private response DTOs shared across partials
    // ---------------------------------------------------------------------

    private sealed class DockerNetworkCreateResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Warning { get; set; } = string.Empty;
    }

    private sealed class DockerSecretCreateResponse
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class DockerSecretListItem
    {
        public string? ID { get; set; }
        public DockerSecretSpec? Spec { get; set; }
    }

    private sealed class DockerSecretSpec
    {
        public string? Name { get; set; }
    }

    private sealed class DockerServiceCreateResponse
    {
        public string ID { get; set; } = string.Empty;
    }
}
