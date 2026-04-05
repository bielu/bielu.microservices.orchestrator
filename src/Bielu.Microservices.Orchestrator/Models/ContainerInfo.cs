namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Represents information about a container across different runtimes.
/// </summary>
public class ContainerInfo
{
    /// <summary>
    /// The unique identifier of the container.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The name of the container.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The image used to create the container.
    /// </summary>
    public string Image { get; set; } = string.Empty;

    /// <summary>
    /// The current state of the container.
    /// </summary>
    public ContainerState State { get; set; }

    /// <summary>
    /// The time the container was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Labels associated with the container.
    /// </summary>
    public IDictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Port mappings for the container.
    /// </summary>
    public IList<PortMapping> Ports { get; set; } = new List<PortMapping>();

    /// <summary>
    /// Environment variables set in the container.
    /// </summary>
    public IDictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string>();
}
