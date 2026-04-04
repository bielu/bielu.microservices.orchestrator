namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Request to create a new container.
/// </summary>
public class CreateContainerRequest
{
    /// <summary>
    /// The name for the container.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The image to use for the container.
    /// </summary>
    public string Image { get; set; } = string.Empty;

    /// <summary>
    /// The command to run in the container.
    /// </summary>
    public IList<string>? Command { get; set; }

    /// <summary>
    /// Environment variables for the container.
    /// </summary>
    public IDictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Port mappings for the container.
    /// </summary>
    public IList<PortMapping> Ports { get; set; } = new List<PortMapping>();

    /// <summary>
    /// Labels for the container.
    /// </summary>
    public IDictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Volume bindings (host:container format).
    /// </summary>
    public IList<string> Volumes { get; set; } = new List<string>();

    /// <summary>
    /// Whether to automatically remove the container when it stops.
    /// </summary>
    public bool AutoRemove { get; set; }
}
