using System.Diagnostics.Metrics;
using System.Reflection;

namespace Bielu.Microservices.Orchestrator.OpenTelemetry;

/// <summary>
/// Provides the <see cref="Meter"/> used for recording orchestrator metrics.
/// </summary>
public static class OrchestratorMetrics
{
    private static readonly AssemblyName AssemblyName =
        typeof(OrchestratorMetrics).Assembly.GetName();

    /// <summary>
    /// The name of the meter.
    /// </summary>
    public static readonly string Name = AssemblyName.Name!;

    /// <summary>
    /// The version of the meter.
    /// </summary>
    public static readonly string Version = AssemblyName.Version?.ToString() ?? "0.0.0.0";

    /// <summary>
    /// The <see cref="Meter"/> for all orchestrator metrics.
    /// </summary>
    public static readonly Meter Meter = new(Name, Version);

    // ---- Container metrics ----

    internal static readonly Counter<long> ContainerOperationCount = Meter.CreateCounter<long>(
        "orchestrator.container.operations",
        description: "Total number of container operations");

    internal static readonly Histogram<double> ContainerOperationDuration = Meter.CreateHistogram<double>(
        "orchestrator.container.operation.duration",
        unit: "s",
        description: "Duration of container operations");

    // ---- Image metrics ----

    internal static readonly Counter<long> ImageOperationCount = Meter.CreateCounter<long>(
        "orchestrator.image.operations",
        description: "Total number of image operations");

    internal static readonly Histogram<double> ImageOperationDuration = Meter.CreateHistogram<double>(
        "orchestrator.image.operation.duration",
        unit: "s",
        description: "Duration of image operations");

    // ---- Network metrics ----

    internal static readonly Counter<long> NetworkOperationCount = Meter.CreateCounter<long>(
        "orchestrator.network.operations",
        description: "Total number of network operations");

    internal static readonly Histogram<double> NetworkOperationDuration = Meter.CreateHistogram<double>(
        "orchestrator.network.operation.duration",
        unit: "s",
        description: "Duration of network operations");

    // ---- Volume metrics ----

    internal static readonly Counter<long> VolumeOperationCount = Meter.CreateCounter<long>(
        "orchestrator.volume.operations",
        description: "Total number of volume operations");

    internal static readonly Histogram<double> VolumeOperationDuration = Meter.CreateHistogram<double>(
        "orchestrator.volume.operation.duration",
        unit: "s",
        description: "Duration of volume operations");

    // ---- Error metrics ----

    internal static readonly Counter<long> OperationErrorCount = Meter.CreateCounter<long>(
        "orchestrator.operation.errors",
        description: "Total number of failed operations");
}
