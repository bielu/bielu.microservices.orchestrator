namespace Bielu.Microservices.Orchestrator.Models;

/// <summary>
/// Request to pull a container image.
/// </summary>
public class PullImageRequest
{
    /// <summary>
    /// The image name (e.g., "nginx").
    /// </summary>
    public string Image { get; set; } = string.Empty;

    /// <summary>
    /// The tag to pull (e.g., "latest").
    /// </summary>
    public string Tag { get; set; } = "latest";

    /// <summary>
    /// Optional registry authentication credentials.
    /// </summary>
    public RegistryCredentials? Credentials { get; set; }
}
