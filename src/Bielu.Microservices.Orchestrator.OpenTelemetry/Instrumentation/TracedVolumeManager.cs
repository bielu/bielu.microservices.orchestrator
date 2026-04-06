using System.Diagnostics;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.OpenTelemetry.Instrumentation;

/// <summary>
/// Decorator for <see cref="IVolumeManager"/> that adds OpenTelemetry tracing and metrics to all operations.
/// </summary>
public class TracedVolumeManager(IVolumeManager inner) : IVolumeManager
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<VolumeInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.VolumeList);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var result = await inner.ListAsync(cancellationToken);
            activity?.SetTag("volume.list.count", result.Count);
            RecordSuccess(OrchestratorActivitySource.VolumeList, startTimestamp);
            return result;
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.VolumeList, startTimestamp, activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<VolumeInfo> CreateAsync(string name, string? driver = null, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.VolumeCreate);
        activity?.SetTag(OrchestratorActivitySource.AttributeVolumeName, name);
        if (driver != null)
        {
            activity?.SetTag(OrchestratorActivitySource.AttributeVolumeDriver, driver);
        }
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var result = await inner.CreateAsync(name, driver, cancellationToken);
            RecordSuccess(OrchestratorActivitySource.VolumeCreate, startTimestamp);
            return result;
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.VolumeCreate, startTimestamp, activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string name, bool force = false, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.VolumeRemove);
        activity?.SetTag(OrchestratorActivitySource.AttributeVolumeName, name);
        activity?.SetTag("volume.remove.force", force);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await inner.RemoveAsync(name, force, cancellationToken);
            RecordSuccess(OrchestratorActivitySource.VolumeRemove, startTimestamp);
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.VolumeRemove, startTimestamp, activity, ex);
            throw;
        }
    }

    private static void RecordSuccess(string operation, long startTimestamp)
    {
        var duration = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        var tags = new TagList { { "operation", operation }, { "status", "success" } };
        OrchestratorMetrics.VolumeOperationCount.Add(1, tags);
        OrchestratorMetrics.VolumeOperationDuration.Record(duration, tags);
    }

    private static void RecordError(string operation, long startTimestamp, Activity? activity, Exception ex)
    {
        var duration = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        var tags = new TagList { { "operation", operation }, { "status", "error" } };
        OrchestratorMetrics.VolumeOperationCount.Add(1, tags);
        OrchestratorMetrics.VolumeOperationDuration.Record(duration, tags);
        OrchestratorMetrics.OperationErrorCount.Add(1, new TagList { { "operation", operation } });
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    }
}
