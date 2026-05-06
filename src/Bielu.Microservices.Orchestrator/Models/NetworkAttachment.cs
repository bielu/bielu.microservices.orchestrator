namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Represents a network attachment for a container.
/// </summary>
public class NetworkAttachment
{
    /// <summary>
    /// The name of the network.
    /// </summary>
    public string NetworkName { get; set; } = string.Empty;

    /// <summary>
    /// Network-scoped aliases for the container.
    /// </summary>
    public IList<string> Aliases { get; set; } = new List<string>();

    /// <summary>
    /// Optional static IPv4 address to assign to the container on this network.
    /// </summary>
    public string? IPv4Address { get; set; }

    /// <summary>
    /// Optional static IPv6 address to assign to the container on this network.
    /// </summary>
    public string? IPv6Address { get; set; }

    /// <summary>
    /// Optional gateway address to set for the container on this network.
    /// </summary>
    public string? Gateway { get; set; }

    /// <summary>
    /// Optional MAC address to assign to the container on this network.
    /// </summary>
    public string? MacAddress { get; set; }

    /// <summary>
    /// Optional list of legacy container links for this network.
    /// </summary>
    public IList<string> Links { get; set; } = new List<string>();

    /// <summary>
    /// Optional driver-specific options for this endpoint.
    /// </summary>
    public IDictionary<string, string> DriverOptions { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Optional list of DNS names associated to this endpoint.
    /// </summary>
    public IList<string> DnsNames { get; set; } = new List<string>();
}
