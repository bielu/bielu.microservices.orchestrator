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
}
