using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.Abstractions;

/// <summary>
/// Detects whether a newer version of a managed instance's image is available
/// and recreates the instance's containers to adopt the new image.
/// </summary>
public interface IImageUpdateService
{
    /// <summary>
    /// Checks whether an image update is available for the given managed instance.
    /// </summary>
    /// <param name="instanceId">The managed instance identifier.</param>
    /// <param name="options">Check options (e.g. whether to pull first).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ImageUpdateStatus> CheckAsync(
        string instanceId,
        ImageUpdateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks every managed instance and returns the per-instance status.
    /// </summary>
    Task<IReadOnlyList<ImageUpdateStatus>> CheckAllAsync(
        ImageUpdateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls the latest image and, if the digest changed (or <see cref="ImageUpdateOptions.Force"/> is set),
    /// removes the existing containers of the instance and recreates them from the persisted
    /// <see cref="ManagedInstance.OriginalRequest"/>.
    /// </summary>
    Task<ImageUpdateResult> UpdateAsync(
        string instanceId,
        ImageUpdateOptions? options = null,
        CancellationToken cancellationToken = default);
}
