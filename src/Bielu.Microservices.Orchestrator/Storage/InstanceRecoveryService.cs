using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Utilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Storage;

/// <summary>
/// Hosted service that runs on startup to reconcile the desired state in the
/// <see cref="IInstanceStore"/> with the actual state of containers in the runtime.
/// </summary>
public class InstanceRecoveryService(
    IInstanceStore instanceStore,
    IContainerManager containerManager,
    ILogger<InstanceRecoveryService> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Instance recovery service starting — reconciling desired state with runtime");

        IReadOnlyList<ManagedInstance> instances;
        try
        {
            instances = await instanceStore.GetAllAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load managed instances from store; skipping recovery");
            return;
        }

        if (instances.Count == 0)
        {
            logger.LogInformation("No managed instances found in store; nothing to reconcile");
            return;
        }

        logger.LogInformation("Found {Count} managed instance(s) to reconcile", instances.Count);

        foreach (var instance in instances)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                await ReconcileInstanceAsync(instance, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reconcile instance {InstanceId}", LogSanitizer.Sanitize(instance.Id));
            }
        }

        logger.LogInformation("Instance recovery service completed reconciliation");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ReconcileInstanceAsync(ManagedInstance instance, CancellationToken cancellationToken)
    {
        switch (instance.DesiredState)
        {
            case DesiredState.Removed:
                logger.LogInformation("Instance {InstanceId} is marked for removal; cleaning up store record",
                    LogSanitizer.Sanitize(instance.Id));
                await instanceStore.RemoveAsync(instance.Id, cancellationToken);
                break;

            case DesiredState.Running:
                await ReconcileRunningInstanceAsync(instance, cancellationToken);
                break;

            case DesiredState.Stopped:
                // For stopped instances we just verify the record is consistent — no re-creation needed
                logger.LogDebug("Instance {InstanceId} desired state is Stopped; no action taken",
                    LogSanitizer.Sanitize(instance.Id));
                break;
        }
    }

    private async Task ReconcileRunningInstanceAsync(ManagedInstance instance, CancellationToken cancellationToken)
    {
        var foundContainerIds = new List<string>();

        foreach (var containerId in instance.ContainerIds)
        {
            var info = await containerManager.GetAsync(containerId, cancellationToken);
            if (info != null)
            {
                foundContainerIds.Add(containerId);

                if (info.State is ContainerState.Exited or ContainerState.Created)
                {
                    logger.LogInformation("Re-starting container {ContainerId} for instance {InstanceId}",
                        LogSanitizer.Sanitize(containerId), LogSanitizer.Sanitize(instance.Id));
                    await containerManager.StartAsync(containerId, cancellationToken);
                }
            }
        }

        var missingCount = instance.ContainerIds.Count - foundContainerIds.Count;

        if (missingCount > 0)
        {
            logger.LogWarning("{MissingCount} container(s) missing for instance {InstanceId}; re-creating from original request",
                missingCount, LogSanitizer.Sanitize(instance.Id));

            for (var i = 0; i < missingCount; i++)
            {
                var newId = await containerManager.CreateAsync(instance.OriginalRequest, cancellationToken);
                await containerManager.StartAsync(newId, cancellationToken);
                foundContainerIds.Add(newId);
            }
        }

        if (foundContainerIds.Count != instance.ContainerIds.Count ||
            !foundContainerIds.SequenceEqual(instance.ContainerIds))
        {
            await instanceStore.UpdateContainerIdsAsync(instance.Id, foundContainerIds.AsReadOnly(), cancellationToken);
        }
    }
}
