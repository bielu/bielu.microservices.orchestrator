using System.Diagnostics.Metrics;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.OpenTelemetry.Instrumentation;

/// <summary>
/// Background service that periodically collects orchestrator and host gauge metrics
/// and exposes them via OpenTelemetry observable instruments.
/// </summary>
public sealed class OrchestratorMetricsCollector(
    IInstanceStore instanceStore,
    IContainerManager containerManager,
    ILogger<OrchestratorMetricsCollector> logger) : IHostedService, IDisposable
{
    private Timer? _timer;
    private readonly HostMetricsProvider _hostMetrics = new();

    // --- Cached snapshot values (read by observable gauge callbacks) ---

    private int _totalManagedInstances;
    private int _healthyInstances;
    private int _totalContainers;
    private int _totalDesiredReplicas;
    private double _cpuUsagePercent;
    private double _memoryUsagePercent;
    private long _memoryAvailableBytes;
    private long _memoryTotalBytes;

    // Breakdowns
    private IReadOnlyList<Measurement<int>> _instancesByStateMeasurements = [];
    private IReadOnlyList<Measurement<int>> _instancesByProviderMeasurements = [];
    private IReadOnlyList<Measurement<int>> _containersByImageMeasurements = [];
    private IReadOnlyList<Measurement<int>> _containersByStateMeasurements = [];

    // Expose for testing
    private IReadOnlyDictionary<string, int> _instancesByState = new Dictionary<string, int>();
    private IReadOnlyDictionary<string, int> _instancesByProvider = new Dictionary<string, int>();
    private IReadOnlyDictionary<string, int> _containersByImage = new Dictionary<string, int>();
    private IReadOnlyDictionary<string, int> _containersByState = new Dictionary<string, int>();

    // --- Observable gauge registrations ---
    // These are created lazily in RegisterGauges() and held to prevent GC collection.
#pragma warning disable IDE0052 // Remove unread private members — fields prevent GC of observable instruments
    private ObservableGauge<int>? _managedInstancesGauge;
    private ObservableGauge<int>? _healthyInstancesGauge;
    private ObservableGauge<int>? _totalContainersGauge;
    private ObservableGauge<int>? _totalDesiredReplicasGauge;
    private ObservableGauge<double>? _cpuUsageGauge;
    private ObservableGauge<double>? _memoryUsageGauge;
    private ObservableGauge<long>? _memoryAvailableGauge;
    private ObservableGauge<long>? _memoryTotalGauge;
    private ObservableGauge<int>? _instancesByStateGauge;
    private ObservableGauge<int>? _instancesByProviderGauge;
    private ObservableGauge<int>? _containersByImageGauge;
    private ObservableGauge<int>? _containersByStateGauge;
#pragma warning restore IDE0052

    /// <summary>
    /// Default collection interval.
    /// </summary>
    internal static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Orchestrator metrics collector starting with {Interval}s interval", DefaultInterval.TotalSeconds);
        RegisterGauges();
        _timer = new Timer(CollectMetricsCallback, null, TimeSpan.Zero, DefaultInterval);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Orchestrator metrics collector stopping");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer?.Dispose();
    }

    // --- Internal accessors for testing ---

    internal int TotalManagedInstances => _totalManagedInstances;
    internal int HealthyInstances => _healthyInstances;
    internal int TotalContainers => _totalContainers;
    internal int TotalDesiredReplicas => _totalDesiredReplicas;
    internal double CpuUsagePercent => _cpuUsagePercent;
    internal double MemoryUsagePercent => _memoryUsagePercent;
    internal long MemoryAvailableBytes => _memoryAvailableBytes;
    internal long MemoryTotalBytes => _memoryTotalBytes;
    internal IReadOnlyDictionary<string, int> InstancesByState => _instancesByState;
    internal IReadOnlyDictionary<string, int> InstancesByProvider => _instancesByProvider;
    internal IReadOnlyDictionary<string, int> ContainersByImage => _containersByImage;
    internal IReadOnlyDictionary<string, int> ContainersByState => _containersByState;

    private void RegisterGauges()
    {
        var meter = OrchestratorMetrics.Meter;

        // Scalar gauges
        _managedInstancesGauge = meter.CreateObservableGauge(
            "orchestrator.managed_instances.total",
            () => _totalManagedInstances,
            description: "Total number of managed microservice instances");

        _healthyInstancesGauge = meter.CreateObservableGauge(
            "orchestrator.managed_instances.healthy",
            () => _healthyInstances,
            description: "Number of managed instances with all containers running");

        _totalContainersGauge = meter.CreateObservableGauge(
            "orchestrator.containers.total",
            () => _totalContainers,
            description: "Total number of individual containers across all managed instances");

        _totalDesiredReplicasGauge = meter.CreateObservableGauge(
            "orchestrator.managed_instances.desired_replicas.total",
            () => _totalDesiredReplicas,
            description: "Total number of desired replicas across all managed instances");

        // Host gauges
        _cpuUsageGauge = meter.CreateObservableGauge(
            "orchestrator.host.cpu.usage",
            () => _cpuUsagePercent,
            unit: "%",
            description: "Host CPU usage percentage");

        _memoryUsageGauge = meter.CreateObservableGauge(
            "orchestrator.host.memory.usage",
            () => _memoryUsagePercent,
            unit: "%",
            description: "Host memory usage percentage");

        _memoryAvailableGauge = meter.CreateObservableGauge(
            "orchestrator.host.memory.available_bytes",
            () => _memoryAvailableBytes,
            unit: "By",
            description: "Available host memory in bytes");

        _memoryTotalGauge = meter.CreateObservableGauge(
            "orchestrator.host.memory.total_bytes",
            () => _memoryTotalBytes,
            unit: "By",
            description: "Total host memory in bytes");

        // Tagged breakdown gauges
        _instancesByStateGauge = meter.CreateObservableGauge(
            "orchestrator.managed_instances.by_state",
            () => _instancesByStateMeasurements,
            description: "Number of managed instances grouped by state (healthy, unhealthy, stopped, removed)");

        _instancesByProviderGauge = meter.CreateObservableGauge(
            "orchestrator.managed_instances.by_provider",
            () => _instancesByProviderMeasurements,
            description: "Number of managed instances grouped by runtime provider");

        _containersByImageGauge = meter.CreateObservableGauge(
            "orchestrator.containers.by_image",
            () => _containersByImageMeasurements,
            description: "Number of containers grouped by image name and version");

        _containersByStateGauge = meter.CreateObservableGauge(
            "orchestrator.containers.by_state",
            () => _containersByStateMeasurements,
            description: "Number of containers grouped by container state");
    }

    private void CollectMetricsCallback(object? state)
    {
        // Fire-and-forget; exceptions are logged but do not crash the timer
        _ = CollectMetricsAsync();
    }

    internal async Task CollectMetricsAsync()
    {
        try
        {
            await CollectOrchestratorMetricsAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to collect orchestrator metrics");
        }

        try
        {
            CollectHostMetrics();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to collect host metrics");
        }
    }

    private async Task CollectOrchestratorMetricsAsync()
    {
        var instances = await instanceStore.GetAllAsync();
        var runningInstances = instances.Where(i => i.DesiredState == DesiredState.Running).ToList();

        _totalManagedInstances = instances.Count;
        _totalDesiredReplicas = instances.Sum(i => i.DesiredReplicas);

        // Instances by provider
        var byProvider = instances
            .GroupBy(i => string.IsNullOrEmpty(i.ProviderName) ? "unknown" : i.ProviderName)
            .ToDictionary(g => g.Key, g => g.Count());
        _instancesByProvider = byProvider;
        _instancesByProviderMeasurements = byProvider
            .Select(kvp => new Measurement<int>(kvp.Value,
                new KeyValuePair<string, object?>("provider", kvp.Key)))
            .ToList();

        // Fetch actual containers for health and state breakdown
        IReadOnlyList<ContainerInfo> containers;
        try
        {
            containers = await containerManager.ListAsync(all: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to list containers for metrics; using cached values");
            return;
        }

        _totalContainers = containers.Count;

        // Containers by state
        var cByState = containers
            .GroupBy(c => c.State.ToString())
            .ToDictionary(g => g.Key, g => g.Count());
        _containersByState = cByState;
        _containersByStateMeasurements = cByState
            .Select(kvp => new Measurement<int>(kvp.Value,
                new KeyValuePair<string, object?>("state", kvp.Key)))
            .ToList();

        // Containers by image (with image and version tags for filtering)
        var cByImage = containers
            .GroupBy(c => c.Image)
            .ToDictionary(g => g.Key, g => g.Count());
        _containersByImage = cByImage;
        _containersByImageMeasurements = cByImage
            .Select(kvp =>
            {
                ParseImageAndVersion(kvp.Key, out var imageName, out var imageVersion);
                return new Measurement<int>(kvp.Value,
                    new KeyValuePair<string, object?>("image", imageName),
                    new KeyValuePair<string, object?>("version", imageVersion));
            })
            .ToList();

        // Determine healthy instances: all containers for the instance are Running
        var containerLookup = containers.ToDictionary(c => c.Id, c => c);
        var healthy = 0;
        var instancesByState = new Dictionary<string, int>();

        foreach (var instance in runningInstances)
        {
            var allRunning = instance.ContainerIds.Count > 0 &&
                             instance.ContainerIds.All(id =>
                                 containerLookup.TryGetValue(id, out var info) &&
                                 info.State == ContainerState.Running);

            var stateLabel = allRunning ? "healthy" : "unhealthy";
            if (allRunning)
                healthy++;

            instancesByState.TryGetValue(stateLabel, out var count);
            instancesByState[stateLabel] = count + 1;
        }

        // Also count stopped/removed desired states
        foreach (var instance in instances.Where(i => i.DesiredState != DesiredState.Running))
        {
            var stateLabel = instance.DesiredState.ToString().ToLowerInvariant();
            instancesByState.TryGetValue(stateLabel, out var count);
            instancesByState[stateLabel] = count + 1;
        }

        _healthyInstances = healthy;
        _instancesByState = instancesByState;
        _instancesByStateMeasurements = instancesByState
            .Select(kvp => new Measurement<int>(kvp.Value,
                new KeyValuePair<string, object?>("state", kvp.Key)))
            .ToList();
    }

    private void CollectHostMetrics()
    {
        _cpuUsagePercent = _hostMetrics.GetCpuUsagePercent();
        _memoryUsagePercent = HostMetricsProvider.GetMemoryUsagePercent();
        _memoryAvailableBytes = HostMetricsProvider.GetAvailableMemoryBytes();
        _memoryTotalBytes = HostMetricsProvider.GetTotalMemoryBytes();
    }

    /// <summary>
    /// Parses an image reference like "nginx:1.25" or "myregistry.io/app:v2.0" into
    /// name and version components.
    /// </summary>
    internal static void ParseImageAndVersion(string imageRef, out string imageName, out string imageVersion)
    {
        if (string.IsNullOrEmpty(imageRef))
        {
            imageName = "unknown";
            imageVersion = "unknown";
            return;
        }

        // Handle images with digest (@sha256:...)
        var digestIndex = imageRef.IndexOf('@');
        if (digestIndex >= 0)
        {
            imageName = imageRef[..digestIndex];
            imageVersion = imageRef[(digestIndex + 1)..];
            return;
        }

        // Handle images with tag (name:tag) — but avoid splitting on registry port (e.g., localhost:5000/app:v1)
        // Find the last colon; if it's after the last slash, it's a tag separator
        var lastSlash = imageRef.LastIndexOf('/');
        var lastColon = imageRef.LastIndexOf(':');

        if (lastColon > lastSlash && lastColon > 0)
        {
            imageName = imageRef[..lastColon];
            imageVersion = imageRef[(lastColon + 1)..];
        }
        else
        {
            imageName = imageRef;
            imageVersion = "latest";
        }
    }
}
