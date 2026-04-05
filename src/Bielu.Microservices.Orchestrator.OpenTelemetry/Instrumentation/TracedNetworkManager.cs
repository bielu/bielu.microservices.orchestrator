using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.OpenTelemetry.Instrumentation;

/// <summary>
/// Decorator for <see cref="INetworkManager"/> that adds OpenTelemetry tracing to all operations.
/// </summary>
public class TracedNetworkManager : INetworkManager
{
    private readonly INetworkManager _inner;

    public TracedNetworkManager(INetworkManager inner)
    {
        _inner = inner;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NetworkInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.NetworkList);

        var result = await _inner.ListAsync(cancellationToken);
        activity?.SetTag("network.list.count", result.Count);
        return result;
    }

    /// <inheritdoc />
    public async Task<string> CreateAsync(string name, string driver = "bridge", CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.NetworkCreate);
        activity?.SetTag("network.name", name);
        activity?.SetTag(OrchestratorActivitySource.AttributeNetworkDriver, driver);

        var networkId = await _inner.CreateAsync(name, driver, cancellationToken);
        activity?.SetTag(OrchestratorActivitySource.AttributeNetworkId, networkId);
        return networkId;
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string networkId, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.NetworkRemove);
        activity?.SetTag(OrchestratorActivitySource.AttributeNetworkId, networkId);

        await _inner.RemoveAsync(networkId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ConnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.NetworkConnect);
        activity?.SetTag(OrchestratorActivitySource.AttributeNetworkId, networkId);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerId, containerId);

        await _inner.ConnectAsync(networkId, containerId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.NetworkDisconnect);
        activity?.SetTag(OrchestratorActivitySource.AttributeNetworkId, networkId);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerId, containerId);

        await _inner.DisconnectAsync(networkId, containerId, cancellationToken);
    }
}
