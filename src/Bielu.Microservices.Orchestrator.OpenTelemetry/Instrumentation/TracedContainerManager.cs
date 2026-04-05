using System.Diagnostics;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.OpenTelemetry.Instrumentation;

/// <summary>
/// Decorator for <see cref="IContainerManager"/> that adds OpenTelemetry tracing to all operations.
/// </summary>
public class TracedContainerManager : IContainerManager
{
    private readonly IContainerManager _inner;

    public TracedContainerManager(IContainerManager inner)
    {
        _inner = inner;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContainerInfo>> ListAsync(bool all = false, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ContainerList);
        activity?.SetTag("container.list.all", all);

        var result = await _inner.ListAsync(all, cancellationToken);
        activity?.SetTag("container.list.count", result.Count);
        return result;
    }

    /// <inheritdoc />
    public async Task<ContainerInfo?> GetAsync(string containerId, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ContainerGet);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerId, containerId);

        return await _inner.GetAsync(containerId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> CreateAsync(CreateContainerRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ContainerCreate);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerImage, request.Image);
        activity?.SetTag("container.name", request.Name);

        var containerId = await _inner.CreateAsync(request, cancellationToken);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerId, containerId);
        return containerId;
    }

    /// <inheritdoc />
    public async Task StartAsync(string containerId, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ContainerStart);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerId, containerId);

        await _inner.StartAsync(containerId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task StopAsync(string containerId, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ContainerStop);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerId, containerId);
        if (timeout.HasValue)
        {
            activity?.SetTag("container.stop.timeout_seconds", (int)timeout.Value.TotalSeconds);
        }

        await _inner.StopAsync(containerId, timeout, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string containerId, bool force = false, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ContainerRemove);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerId, containerId);
        activity?.SetTag("container.remove.force", force);

        await _inner.RemoveAsync(containerId, force, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> GetLogsAsync(string containerId, bool stdout = true, bool stderr = true, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ContainerGetLogs);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerId, containerId);
        activity?.SetTag("container.logs.stdout", stdout);
        activity?.SetTag("container.logs.stderr", stderr);

        return await _inner.GetLogsAsync(containerId, stdout, stderr, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ScaleAsync(string containerId, int replicas, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ContainerScale);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerId, containerId);
        activity?.SetTag("container.scale.replicas", replicas);

        await _inner.ScaleAsync(containerId, replicas, cancellationToken);
    }
}
