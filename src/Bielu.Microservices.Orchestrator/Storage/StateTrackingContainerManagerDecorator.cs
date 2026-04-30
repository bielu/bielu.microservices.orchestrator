using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Utilities;
using Microsoft.Extensions.Logging;
// OrchestratorLabels lives in the root Bielu.Microservices.Orchestrator namespace.

namespace Bielu.Microservices.Orchestrator.Storage;

/// <summary>
/// Decorator for <see cref="IContainerManager"/> that automatically persists
/// desired state to an <see cref="IInstanceStore"/> on lifecycle operations.
/// This keeps all provider implementations untouched.
/// </summary>
/// <remarks>
/// The <c>containerId</c> parameter on lifecycle methods is treated as the
/// <see cref="ManagedInstance.Id"/> (typically the user-supplied
/// <see cref="CreateContainerRequest.Name"/>). The decorator looks up the
/// corresponding store record to apply state transitions; if no record is found,
/// the parameter is forwarded as-is to the inner manager and no store update is
/// performed.
/// </remarks>
public class StateTrackingContainerManagerDecorator(
    IContainerManager inner,
    IInstanceStore instanceStore,
    OrchestratorOptions orchestratorOptions,
    ILogger<StateTrackingContainerManagerDecorator> logger) : IContainerManager
{
    /// <summary>
    /// Deferred decorator priority for the state-tracking decorator.
    /// Applied close to the provider (low value = inner wrapper).
    /// </summary>
    public const int DecoratorPriority = 100;

    public string HostAddress => inner.HostAddress;

    public string ProviderName => inner.ProviderName;

    /// <inheritdoc />
    public Task<IReadOnlyList<ContainerInfo>> ListAsync(bool all = false, CancellationToken cancellationToken = default)
        => inner.ListAsync(all, cancellationToken);

    /// <inheritdoc />
    public Task<ContainerInfo?> GetAsync(string containerId, CancellationToken cancellationToken = default)
        => inner.GetAsync(containerId, cancellationToken);

    /// <inheritdoc />
    public async Task<string> CreateAsync(CreateContainerRequest request, CancellationToken cancellationToken = default)
    {
        var firstContainerId = await inner.CreateAsync(request, cancellationToken);

        var instanceId = request.Name ?? firstContainerId;

        // For Replicas > 1 the inner manager only returns the first container id.
        // Best-effort: try to discover all replica ids by group label so the store
        // record is consistent with DesiredReplicas. If discovery fails or the
        // provider doesn't support group labels, fall back to the single id.
        var containerIds = await TryDiscoverReplicaIdsAsync(request, instanceId, firstContainerId, cancellationToken);

        var instance = new ManagedInstance
        {
            Id = instanceId,
            OrchestratorId = orchestratorOptions.OrchestratorId,
            ContainerIds = containerIds,
            OriginalRequest = request,
            DesiredState = DesiredState.Running,
            DesiredReplicas = request.Replicas,
            ProviderName = inner.ProviderName,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            await instanceStore.SaveAsync(instance, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist managed instance {InstanceId} to store", LogSanitizer.Sanitize(instance.Id));
        }

        return firstContainerId;
    }

    /// <inheritdoc />
    public async Task StartAsync(string containerId, CancellationToken cancellationToken = default)
    {
        await inner.StartAsync(containerId, cancellationToken);
        await TryUpdateDesiredStateAsync(containerId, DesiredState.Running, cancellationToken);
    }

    /// <inheritdoc />
    public async Task StopAsync(string containerId, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        await inner.StopAsync(containerId, timeout, cancellationToken);
        await TryUpdateDesiredStateAsync(containerId, DesiredState.Stopped, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string containerId, bool force = false, CancellationToken cancellationToken = default)
    {
        // Tombstone first: mark DesiredState=Removed so a crash between the runtime
        // call and the store delete still leaves a clear intent for reconciliation.
        await TryUpdateDesiredStateAsync(containerId, DesiredState.Removed, cancellationToken);

        // Then perform the actual runtime removal.
        await inner.RemoveAsync(containerId, force, cancellationToken);

        // Finally, drop the store record now that the runtime container is gone.
        try
        {
            await instanceStore.RemoveAsync(containerId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete managed instance {InstanceId} from store after runtime removal", LogSanitizer.Sanitize(containerId));
        }
    }

    /// <inheritdoc />
    public Task<string> GetLogsAsync(string containerId, bool stdout = true, bool stderr = true, CancellationToken cancellationToken = default)
        => inner.GetLogsAsync(containerId, stdout, stderr, cancellationToken);

    /// <inheritdoc />
    public async Task ScaleAsync(string containerId, int replicas, CancellationToken cancellationToken = default)
    {
        await inner.ScaleAsync(containerId, replicas, cancellationToken);

        try
        {
            var existing = await instanceStore.GetAsync(containerId, cancellationToken);
            if (existing != null)
            {
                existing.DesiredReplicas = replicas;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await instanceStore.SaveAsync(existing, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update desired replicas for {InstanceId} in store", LogSanitizer.Sanitize(containerId));
        }
    }

    private async Task TryUpdateDesiredStateAsync(string instanceId, DesiredState desiredState, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await instanceStore.GetAsync(instanceId, cancellationToken);
            if (existing == null)
            {
                // No tracked record (e.g. container created outside the orchestrator,
                // or already removed). Nothing to update.
                return;
            }

            if (existing.DesiredState == desiredState)
            {
                return;
            }

            existing.DesiredState = desiredState;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await instanceStore.SaveAsync(existing, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update desired state for {InstanceId} to {DesiredState} in store",
                LogSanitizer.Sanitize(instanceId), desiredState);
        }
    }

    private async Task<IList<string>> TryDiscoverReplicaIdsAsync(
        CreateContainerRequest request,
        string instanceId,
        string firstContainerId,
        CancellationToken cancellationToken)
    {
        if (request.Replicas <= 1)
        {
            return new List<string> { firstContainerId };
        }

        try
        {
            var groupName = request.Name ?? instanceId;
            var all = await inner.ListAsync(all: true, cancellationToken);
            var replicaIds = (all ?? new List<ContainerInfo>().AsReadOnly())
                .Where(c => c.Labels != null
                            && c.Labels.TryGetValue(OrchestratorLabels.Group, out var g)
                            && string.Equals(g, groupName, StringComparison.Ordinal))
                .Select(c => c.Id)
                .ToList();

            if (replicaIds.Count > 0)
            {
                // Ensure the first id we got from CreateAsync is included even if
                // the listing race didn't see it yet.
                if (!replicaIds.Contains(firstContainerId))
                {
                    replicaIds.Insert(0, firstContainerId);
                }
                return replicaIds;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to discover replica container ids for {InstanceId}; storing only the primary id",
                LogSanitizer.Sanitize(instanceId));
        }

        return new List<string> { firstContainerId };
    }
}
