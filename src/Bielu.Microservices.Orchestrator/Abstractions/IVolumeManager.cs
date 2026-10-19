using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.Abstractions;

/// <summary>
/// Manages container volume operations.
/// </summary>
public interface IVolumeManager
{
    /// <summary>
    /// Lists all volumes.
    /// </summary>
    Task<IReadOnlyList<VolumeInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed information about a specific volume, or <c>null</c> if not found.
    /// </summary>
    Task<VolumeInfo?> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new volume.
    /// </summary>
    /// <param name="name">Volume name.</param>
    /// <param name="driver">Storage driver. Defaults to <c>"local"</c>.</param>
    /// <param name="driverOptions">Driver-specific options (e.g. <c>type</c>, <c>o</c>, <c>device</c> for local-driver bind volumes).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<VolumeInfo> CreateAsync(string name, string? driver = null, IDictionary<string, string>? driverOptions = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a volume.
    /// </summary>
    Task RemoveAsync(string name, bool force = false, CancellationToken cancellationToken = default);
}
