namespace Bielu.Microservices.Orchestrator.Docker.Configuration;

/// <summary>
/// Configuration options for the Docker runtime provider.
/// </summary>
public class DockerOptions
{
    /// <summary>
    /// The Docker Engine API endpoint.
    /// Defaults to the platform-appropriate socket.
    /// </summary>
    public string Endpoint { get; set; } = GetDefaultEndpoint();

    /// <summary>
    /// Connection timeout.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    private static string GetDefaultEndpoint()
    {
        return OperatingSystem.IsWindows()
            ? "npipe://./pipe/docker_engine"
            : "unix:///var/run/docker.sock";
    }
}
