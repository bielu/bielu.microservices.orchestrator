using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Bielu.Microservices.Orchestrator.OpenTelemetry.Instrumentation;

/// <summary>
/// Shared helpers for recording OpenTelemetry metrics across instrumented manager decorators.
/// </summary>
internal static class MetricsHelper
{
    internal static void RecordSuccess(
        string operation,
        long startTimestamp,
        Counter<long> operationCount,
        Histogram<double> operationDuration)
    {
        var duration = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        var tags = new TagList { { "operation", operation }, { "status", "success" } };
        operationCount.Add(1, tags);
        operationDuration.Record(duration, tags);
    }

    internal static void RecordError(
        string operation,
        long startTimestamp,
        Activity? activity,
        Exception ex,
        Counter<long> operationCount,
        Histogram<double> operationDuration)
    {
        var duration = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        var tags = new TagList { { "operation", operation }, { "status", "error" } };
        operationCount.Add(1, tags);
        operationDuration.Record(duration, tags);
        OrchestratorMetrics.OperationErrorCount.Add(1, new TagList { { "operation", operation } });
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    }
}
