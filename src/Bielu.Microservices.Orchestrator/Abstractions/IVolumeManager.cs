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
    /// Creates a new volume.
    /// </summary>
    Task<VolumeInfo> CreateAsync(string name, string? driver = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a volume.
    /// </summary>
    Task RemoveAsync(string name, bool force = false, CancellationToken cancellationToken = default);
}
