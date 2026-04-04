namespace Bielu.Microservices.Orchestrator.Containerd.Configuration;

/// <summary>
/// Configuration options for the containerd runtime provider.
/// </summary>
public class ContainerdOptions
{
    /// <summary>
    /// The containerd gRPC socket endpoint.
    /// </summary>
    public string Endpoint { get; set; } = "unix:///run/containerd/containerd.sock";

    /// <summary>
    /// The default namespace for containerd operations.
    /// </summary>
    public string Namespace { get; set; } = "default";

    /// <summary>
    /// Connection timeout.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
