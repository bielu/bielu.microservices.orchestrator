namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Represents information about a container network.
/// Mirrors the data exposed by the underlying runtime (e.g. Docker's <c>NetworkResponse</c>)
/// so callers have access to the full set of network attributes.
/// </summary>
public class NetworkInfo
{
    /// <summary>
    /// The unique identifier of the network.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The name of the network.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The driver used by the network (e.g., bridge, overlay).
    /// </summary>
    public string Driver { get; set; } = string.Empty;

    /// <summary>
    /// The level at which the network exists (e.g. <c>swarm</c> for cluster-wide,
    /// or <c>local</c> for machine level).
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// Date and time at which the network was created.
    /// </summary>
    public DateTime? Created { get; set; }

    /// <summary>
    /// Whether the network was created with IPv4 enabled.
    /// </summary>
    public bool EnableIPv4 { get; set; }

    /// <summary>
    /// Whether the network was created with IPv6 enabled.
    /// </summary>
    public bool EnableIPv6 { get; set; }

    /// <summary>
    /// Whether the network is created to only allow internal networking connectivity.
    /// </summary>
    public bool Internal { get; set; }

    /// <summary>
    /// Whether a global / swarm scope network is manually attachable by regular
    /// containers from workers in swarm mode.
    /// </summary>
    public bool Attachable { get; set; }

    /// <summary>
    /// Whether the network is providing the routing-mesh for the swarm cluster.
    /// </summary>
    public bool Ingress { get; set; }

    /// <summary>
    /// Whether the network is a config-only network. Config-only networks are
    /// placeholder networks for network configurations to be used by other networks.
    /// </summary>
    public bool ConfigOnly { get; set; }

    /// <summary>
    /// IP Address Management configuration for the network.
    /// </summary>
    public NetworkIpamInfo? Ipam { get; set; }

    /// <summary>
    /// Network-specific options used when creating the network.
    /// </summary>
    public IDictionary<string, string> Options { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Labels associated with the network.
    /// </summary>
    public IDictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Endpoints currently attached to the network, keyed by container id.
    /// Mirrors <c>NetworkResponse.Containers</c> and is used to determine which
    /// IP addresses are already in use when allocating the next free IP.
    /// </summary>
    public IDictionary<string, NetworkEndpointInfo> Containers { get; set; } = new Dictionary<string, NetworkEndpointInfo>();
}

/// <summary>
/// A single endpoint attached to a network (mirrors Docker's <c>EndpointResource</c>).
/// </summary>
public class NetworkEndpointInfo
{
    /// <summary>
    /// Friendly name of the container holding the endpoint.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Unique endpoint identifier on the network.
    /// </summary>
    public string EndpointId { get; set; } = string.Empty;

    /// <summary>
    /// MAC address of the endpoint.
    /// </summary>
    public string MacAddress { get; set; } = string.Empty;

    /// <summary>
    /// IPv4 address assigned to the endpoint, in CIDR notation (e.g. <c>172.19.0.2/16</c>).
    /// </summary>
    public string IPv4Address { get; set; } = string.Empty;

    /// <summary>
    /// IPv6 address assigned to the endpoint, in CIDR notation.
    /// </summary>
    public string IPv6Address { get; set; } = string.Empty;
}

/// <summary>
/// IP Address Management configuration for a network.
/// </summary>
public class NetworkIpamInfo
{
    /// <summary>
    /// The name of the IPAM driver (e.g. <c>default</c>).
    /// </summary>
    public string Driver { get; set; } = string.Empty;

    /// <summary>
    /// IPAM driver-specific options.
    /// </summary>
    public IDictionary<string, string> Options { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// IPAM configuration entries (subnets / gateways / IP ranges).
    /// </summary>
    public IList<NetworkIpamConfig> Config { get; set; } = new List<NetworkIpamConfig>();
}

/// <summary>
/// A single IPAM configuration entry for a network.
/// </summary>
public class NetworkIpamConfig
{
    /// <summary>
    /// The subnet, in CIDR notation (e.g. <c>172.20.0.0/16</c>).
    /// </summary>
    public string Subnet { get; set; } = string.Empty;

    /// <summary>
    /// The IP range within the subnet from which container IPs may be allocated.
    /// </summary>
    public string IPRange { get; set; } = string.Empty;

    /// <summary>
    /// The gateway IP for the subnet.
    /// </summary>
    public string Gateway { get; set; } = string.Empty;

    /// <summary>
    /// Auxiliary addresses to reserve in the subnet (host name → IP).
    /// </summary>
    public IDictionary<string, string> AuxAddress { get; set; } = new Dictionary<string, string>();
}
