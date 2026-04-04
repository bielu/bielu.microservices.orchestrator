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
        if (OperatingSystem.IsWindows())
        {
            return "npipe://./pipe/podman-machine-default";
        }

        var uid = Environment.GetEnvironmentVariable("UID") ?? "1000";
        // Validate UID is numeric to prevent path injection
        if (!int.TryParse(uid, out _))
        {
            uid = "1000";
        }

        return $"unix:///run/user/{uid}/podman/podman.sock";
    }
}
