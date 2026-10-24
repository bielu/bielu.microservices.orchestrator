using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Bielu.Microservices.Orchestrator.OpenTelemetry;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Records the activities and measurements the orchestrator's OpenTelemetry instrumentation emits
/// while it is alive. Listeners are process-wide, so tests using it join the
/// <see cref="TelemetryCollection"/>, which runs them one at a time.
/// </summary>
internal sealed class TelemetryCapture : IDisposable
{
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;

    public TelemetryCapture()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OrchestratorActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = Activities.Add
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == OrchestratorMetrics.Name)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _meterListener.Start();
    }

    public ConcurrentBag<Activity> Activities { get; } = [];

    public ConcurrentBag<CapturedMeasurement> Measurements { get; } = [];

    /// <summary>Returns the single activity recorded for <paramref name="operation"/>.</summary>
    public Activity Single(string operation) => Activities.Single(a => a.OperationName == operation);

    /// <summary>Returns the measurements of <paramref name="instrument"/> tagged with <paramref name="operation"/>.</summary>
    public IReadOnlyList<CapturedMeasurement> For(string instrument, string operation) =>
        Measurements.Where(m => m.Instrument == instrument && m.Tags.GetValueOrDefault("operation") as string == operation).ToList();

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, object?>();
        foreach (var tag in tags)
        {
            copy[tag.Key] = tag.Value;
        }

        Measurements.Add(new CapturedMeasurement(instrument.Name, value, copy));
    }
}

/// <summary>A measurement recorded by <see cref="TelemetryCapture"/>.</summary>
internal sealed record CapturedMeasurement(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);

/// <summary>
/// Serialises the tests that listen to the orchestrator's activity source and meter, so one test's
/// telemetry never shows up in another's capture.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetryCollection
{
    public const string Name = "Telemetry";
}
