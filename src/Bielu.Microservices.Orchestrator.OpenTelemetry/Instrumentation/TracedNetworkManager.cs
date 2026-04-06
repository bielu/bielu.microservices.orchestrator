using System.Diagnostics;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.OpenTelemetry.Instrumentation;

/// <summary>
/// Decorator for <see cref="INetworkManager"/> that adds OpenTelemetry tracing and metrics to all operations.
/// </summary>
public class TracedNetworkManager(INetworkManager inner) : INetworkManager
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<NetworkInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.NetworkList);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var result = await inner.ListAsync(cancellationToken);
            activity?.SetTag("network.list.count", result.Count);
            RecordSuccess(OrchestratorActivitySource.NetworkList, startTimestamp);
            return result;
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.NetworkList, startTimestamp, activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> CreateAsync(string name, string driver = "bridge", CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.NetworkCreate);
        activity?.SetTag("network.name", name);
        activity?.SetTag(OrchestratorActivitySource.AttributeNetworkDriver, driver);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var networkId = await inner.CreateAsync(name, driver, cancellationToken);
            activity?.SetTag(OrchestratorActivitySource.AttributeNetworkId, networkId);
            RecordSuccess(OrchestratorActivitySource.NetworkCreate, startTimestamp);
            return networkId;
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.NetworkCreate, startTimestamp, activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string networkId, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.NetworkRemove);
        activity?.SetTag(OrchestratorActivitySource.AttributeNetworkId, networkId);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await inner.RemoveAsync(networkId, cancellationToken);
            RecordSuccess(OrchestratorActivitySource.NetworkRemove, startTimestamp);
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.NetworkRemove, startTimestamp, activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ConnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.NetworkConnect);
        activity?.SetTag(OrchestratorActivitySource.AttributeNetworkId, networkId);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerId, containerId);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await inner.ConnectAsync(networkId, containerId, cancellationToken);
            RecordSuccess(OrchestratorActivitySource.NetworkConnect, startTimestamp);
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.NetworkConnect, startTimestamp, activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.NetworkDisconnect);
        activity?.SetTag(OrchestratorActivitySource.AttributeNetworkId, networkId);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerId, containerId);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await inner.DisconnectAsync(networkId, containerId, cancellationToken);
            RecordSuccess(OrchestratorActivitySource.NetworkDisconnect, startTimestamp);
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.NetworkDisconnect, startTimestamp, activity, ex);
            throw;
        }
    }

    private static void RecordSuccess(string operation, long startTimestamp)
    {
        var duration = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        var tags = new TagList { { "operation", operation }, { "status", "success" } };
        OrchestratorMetrics.NetworkOperationCount.Add(1, tags);
        OrchestratorMetrics.NetworkOperationDuration.Record(duration, tags);
    }

    private static void RecordError(string operation, long startTimestamp, Activity? activity, Exception ex)
    {
        var duration = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        var tags = new TagList { { "operation", operation }, { "status", "error" } };
        OrchestratorMetrics.NetworkOperationCount.Add(1, tags);
        OrchestratorMetrics.NetworkOperationDuration.Record(duration, tags);
        OrchestratorMetrics.OperationErrorCount.Add(1, new TagList { { "operation", operation } });
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    }
}
