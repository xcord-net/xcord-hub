namespace XcordHub.Infrastructure.Services;

public sealed class DockerOptions
{
    public string SocketProxyUrl { get; set; } = "http://docker-socket-proxy:2375";
    public bool UseReal { get; set; } = false;

    /// <summary>
    /// The Docker image to use when provisioning xcord-fed instance containers.
    /// Defaults to "xcord-fed:latest". Override in E2E to "xcord-instance:e2e".
    /// </summary>
    public string InstanceImage { get; set; } = "xcord-fed:latest";
}
