using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.Abstractions;

/// <summary>
/// Persists the desired state of managed instances so the orchestrator
/// can recover and reconcile after a restart.
/// </summary>
public interface IInstanceStore
{
    /// <summary>
    /// Saves or updates a managed instance record.
    /// </summary>
    Task SaveAsync(ManagedInstance instance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a managed instance by its identifier.
    /// </summary>
    /// <returns>The instance, or <c>null</c> if not found.</returns>
    Task<ManagedInstance?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all managed instance records.
    /// </summary>
    Task<IReadOnlyList<ManagedInstance>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a managed instance record by its identifier.
    /// </summary>
    Task RemoveAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates only the container IDs for an existing instance (e.g. after re-creation).
    /// </summary>
    Task UpdateContainerIdsAsync(string id, IReadOnlyList<string> containerIds, CancellationToken cancellationToken = default);
}
