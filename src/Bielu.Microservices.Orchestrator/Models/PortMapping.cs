namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Represents a port mapping between host and container.
/// </summary>
public class PortMapping
{
    /// <summary>
    /// The port number inside the container.
    /// </summary>
    public int ContainerPort { get; set; }

    /// <summary>
    /// The port number on the host.
    /// </summary>
    public int HostPort { get; set; }

    /// <summary>
    /// The protocol (tcp, udp).
    /// </summary>
    public string Protocol { get; set; } = "tcp";

    /// <summary>
    /// The host IP to bind to.
    /// </summary>
    public string HostIp { get; set; } = "0.0.0.0";
}
