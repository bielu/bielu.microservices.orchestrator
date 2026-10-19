namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Options controlling an image update check or update operation.
/// </summary>
public class ImageUpdateOptions
{
    /// <summary>
    /// When <c>true</c> (default), pulls the image from the registry before comparing digests.
    /// </summary>
    public bool Pull { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, recreates the container even if no digest change is detected.
    /// </summary>
    public bool Force { get; set; }
}

/// <summary>
/// Result of an image update availability check for a managed instance.
/// </summary>
public class ImageUpdateStatus
{
    /// <summary>The managed instance identifier the check was performed for.</summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>The image reference (e.g. "nginx:1.25") associated with the instance.</summary>
    public string Image { get; set; } = string.Empty;

    /// <summary>The image digest recorded on the running container(s), if any.</summary>
    public string? CurrentDigest { get; set; }

    /// <summary>The image digest currently available locally, after an optional pull.</summary>
    public string? LatestDigest { get; set; }

    /// <summary>True when <see cref="LatestDigest"/> differs from <see cref="CurrentDigest"/>.</summary>
    public bool UpdateAvailable { get; set; }
}

/// <summary>
/// Result of an image update operation.
/// </summary>
public class ImageUpdateResult
{
    /// <summary>True when the instance's containers were recreated using the new image.</summary>
    public bool Updated { get; set; }

    /// <summary>The managed instance identifier.</summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>The image digest the instance was running before the operation.</summary>
    public string? PreviousDigest { get; set; }

    /// <summary>The image digest the instance is running after the operation.</summary>
    public string? NewDigest { get; set; }

    /// <summary>Runtime container identifiers after the operation.</summary>
    public IList<string> ContainerIds { get; set; } = new List<string>();
}
