namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Represents information about a container volume.
/// </summary>
public class VolumeInfo
{
    /// <summary>
    /// The name of the volume.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The driver used by the volume.
    /// </summary>
    public string Driver { get; set; } = string.Empty;

    /// <summary>
    /// The mount point of the volume on the host.
    /// </summary>
    public string MountPoint { get; set; } = string.Empty;

    /// <summary>
    /// Labels associated with the volume.
    /// </summary>
    public IDictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// The time the volume was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
