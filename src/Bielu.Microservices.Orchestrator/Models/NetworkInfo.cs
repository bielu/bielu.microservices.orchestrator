namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Represents information about a container network.
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
    /// Labels associated with the network.
    /// </summary>
    public IDictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
}
