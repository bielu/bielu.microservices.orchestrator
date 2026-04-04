using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.Abstractions;

/// <summary>
/// Manages container image operations.
/// </summary>
public interface IImageManager
{
    /// <summary>
    /// Lists all images.
    /// </summary>
    Task<IReadOnlyList<ImageInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed information about a specific image.
    /// </summary>
    Task<ImageInfo?> GetAsync(string imageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls an image from a registry.
    /// </summary>
    Task PullAsync(PullImageRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an image.
    /// </summary>
    Task RemoveAsync(string imageId, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tags an image with a new name.
    /// </summary>
    Task TagAsync(string imageId, string repository, string tag, CancellationToken cancellationToken = default);
}
