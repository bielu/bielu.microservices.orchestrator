namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Represents information about a container image.
/// </summary>
public class ImageInfo
{
    /// <summary>
    /// The unique identifier of the image.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Repository tags for the image (e.g., "nginx:latest").
    /// </summary>
    public IList<string> Tags { get; set; } = new List<string>();

    /// <summary>
    /// The size of the image in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// The time the image was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Labels associated with the image.
    /// </summary>
    public IDictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
}
