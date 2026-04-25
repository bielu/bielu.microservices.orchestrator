using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Utilities;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Storage;

/// <summary>
/// Decorator for <see cref="IContainerManager"/> that automatically persists
/// desired state to an <see cref="IInstanceStore"/> on create, remove, and scale operations.
/// This keeps all provider implementations untouched.
/// </summary>
public class StateTrackingContainerManagerDecorator(
    IContainerManager inner,
    IInstanceStore instanceStore,
    IContainerOrchestrator orchestrator,
    ILogger<StateTrackingContainerManagerDecorator> logger) : IContainerManager
{
    /// <summary>
    /// Deferred decorator priority for the state-tracking decorator.
    /// Applied close to the provider (low value = inner wrapper).
    /// </summary>
    public const int DecoratorPriority = 100;

    public string HostAddress  => inner.HostAddress;

    /// <inheritdoc />
    public Task<IReadOnlyList<ContainerInfo>> ListAsync(bool all = false, CancellationToken cancellationToken = default)
        => inner.ListAsync(all, cancellationToken);

    /// <inheritdoc />
    public Task<ContainerInfo?> GetAsync(string containerId, CancellationToken cancellationToken = default)
        => inner.GetAsync(containerId, cancellationToken);

    /// <inheritdoc />
    public async Task<string> CreateAsync(CreateContainerRequest request, CancellationToken cancellationToken = default)
    {
        var containerId = await inner.CreateAsync(request, cancellationToken);

        var instance = new ManagedInstance
        {
            Id = request.Name ?? containerId,
            OrchestratorId = Guid.NewGuid(), //todo: make this configurable and stick to machine
            ContainerIds = [containerId],
            OriginalRequest = request,
            DesiredState = DesiredState.Running,
            DesiredReplicas = request.Replicas,
            ProviderName = orchestrator.ProviderName,
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

        return containerId;
    }

    /// <inheritdoc />
    public Task StartAsync(string containerId, CancellationToken cancellationToken = default)
        => inner.StartAsync(containerId, cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(string containerId, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => inner.StopAsync(containerId, timeout, cancellationToken);

    /// <inheritdoc />
    public async Task RemoveAsync(string containerId, bool force = false, CancellationToken cancellationToken = default)
    {
        // Remove from store before runtime removal to avoid phantom records
        try
        {
            await instanceStore.RemoveAsync(containerId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update store before removing container {ContainerId}", LogSanitizer.Sanitize(containerId));
        }

        await inner.RemoveAsync(containerId, force, cancellationToken);
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
            logger.LogWarning(ex, "Failed to update desired replicas for {ContainerId} in store", LogSanitizer.Sanitize(containerId));
        }
    }
}
