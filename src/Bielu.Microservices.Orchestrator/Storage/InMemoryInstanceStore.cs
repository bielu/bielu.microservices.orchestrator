using System.Collections.Concurrent;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.Storage;

/// <summary>
/// In-memory implementation of <see cref="IInstanceStore"/>.
/// Suitable for testing and ephemeral workloads where persistence across restarts is not needed.
/// </summary>
public class InMemoryInstanceStore : IInstanceStore
{
    private readonly ConcurrentDictionary<string, ManagedInstance> _instances = new();

    /// <inheritdoc />
    public Task SaveAsync(ManagedInstance instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(instance.Id);

        instance.UpdatedAt = DateTimeOffset.UtcNow;
        _instances[instance.Id] = instance;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ManagedInstance?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        _instances.TryGetValue(id, out var instance);
        return Task.FromResult(instance);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ManagedInstance>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ManagedInstance> result = _instances.Values.ToList().AsReadOnly();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        _instances.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateContainerIdsAsync(string id, IReadOnlyList<string> containerIds, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(containerIds);

        if (_instances.TryGetValue(id, out var instance))
        {
            instance.ContainerIds = containerIds.ToList();
            instance.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }
}
