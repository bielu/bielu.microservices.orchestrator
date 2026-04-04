namespace Bielu.Microservices.Orchestrator.Podman.Configuration;

/// <summary>
/// Configuration options for the Podman runtime provider.
/// </summary>
public class PodmanOptions
{
    /// <summary>
    /// The Podman API endpoint.
    /// Podman exposes a Docker-compatible API.
    /// </summary>
    public string Endpoint { get; set; } = GetDefaultEndpoint();

    /// <summary>
    /// Connection timeout.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    private static string GetDefaultEndpoint()
    {
        return OperatingSystem.IsWindows()
            ? "npipe://./pipe/podman-machine-default"
            : $"unix:///run/user/{Environment.GetEnvironmentVariable("UID") ?? "1000"}/podman/podman.sock";
    }
}
