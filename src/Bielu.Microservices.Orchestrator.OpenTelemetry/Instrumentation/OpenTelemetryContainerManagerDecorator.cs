using System.Diagnostics;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.OpenTelemetry.Instrumentation;

/// <summary>
/// Decorator for <see cref="IContainerManager"/> that adds OpenTelemetry tracing and metrics to all operations,
/// and automatically injects OTEL environment variables into containers for distributed tracing.
/// </summary>
public class OpenTelemetryContainerManagerDecorator(IContainerManager inner) : IContainerManager
{
    private static readonly string[] OtelEnvironmentVariables =
    [
        "OTEL_BLRP_SCHEDULE_DELAY",
        "OTEL_BSP_SCHEDULE_DELAY",
        "OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION",
        "OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION",
        "OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY",
        "OTEL_EXPORTER_OTLP_ENDPOINT",
        "OTEL_EXPORTER_OTLP_HEADERS",
        "OTEL_EXPORTER_OTLP_PROTOCOL",
        "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT",
        "OTEL_METRIC_EXPORT_INTERVAL",
        "OTEL_METRICS_EXEMPLAR_FILTER" ,
        "OTEL_RESOURCE_ATTRIBUTES",
        "OTEL_SERVICE_NAME",
        "OTEL_TRACES_SAMPLER"
    ];
   


    /// <inheritdoc />
    public async Task<IReadOnlyList<ContainerInfo>> ListAsync(bool all = false, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ContainerList);
        activity?.SetTag("container.list.all", all);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var result = await inner.ListAsync(all, cancellationToken);
            activity?.SetTag("container.list.count", result.Count);
            RecordSuccess(OrchestratorActivitySource.ContainerList, startTimestamp);
            return result;
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.ContainerList, startTimestamp, activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ContainerInfo?> GetAsync(string containerId, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ContainerGet);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerId, containerId);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var result = await inner.GetAsync(containerId, cancellationToken);
            RecordSuccess(OrchestratorActivitySource.ContainerGet, startTimestamp);
            return result;
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.ContainerGet, startTimestamp, activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> CreateAsync(CreateContainerRequest request, CancellationToken cancellationToken = default)
    {
        // Inject OTEL environment variables from the host into the container
        foreach (var envVar in OtelEnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(value) && !request.EnvironmentVariables.ContainsKey(envVar))
            {
                request.EnvironmentVariables[envVar] = value;
            }
        }

        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ContainerCreate);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerImage, request.Image);
        activity?.SetTag("container.name", request.Name);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var containerId = await inner.CreateAsync(request, cancellationToken);
            activity?.SetTag(OrchestratorActivitySource.AttributeContainerId, containerId);
            RecordSuccess(OrchestratorActivitySource.ContainerCreate, startTimestamp);
            return containerId;
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.ContainerCreate, startTimestamp, activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(string containerId, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ContainerStart);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerId, containerId);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await inner.StartAsync(containerId, cancellationToken);
            RecordSuccess(OrchestratorActivitySource.ContainerStart, startTimestamp);
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.ContainerStart, startTimestamp, activity, ex);
            throw;
        }
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
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await inner.StopAsync(containerId, timeout, cancellationToken);
            RecordSuccess(OrchestratorActivitySource.ContainerStop, startTimestamp);
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.ContainerStop, startTimestamp, activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string containerId, bool force = false, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ContainerRemove);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerId, containerId);
        activity?.SetTag("container.remove.force", force);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await inner.RemoveAsync(containerId, force, cancellationToken);
            RecordSuccess(OrchestratorActivitySource.ContainerRemove, startTimestamp);
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.ContainerRemove, startTimestamp, activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> GetLogsAsync(string containerId, bool stdout = true, bool stderr = true, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ContainerGetLogs);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerId, containerId);
        activity?.SetTag("container.logs.stdout", stdout);
        activity?.SetTag("container.logs.stderr", stderr);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var result = await inner.GetLogsAsync(containerId, stdout, stderr, cancellationToken);
            RecordSuccess(OrchestratorActivitySource.ContainerGetLogs, startTimestamp);
            return result;
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.ContainerGetLogs, startTimestamp, activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ScaleAsync(string containerId, int replicas, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestratorActivitySource.Source.StartActivity(OrchestratorActivitySource.ContainerScale);
        activity?.SetTag(OrchestratorActivitySource.AttributeContainerId, containerId);
        activity?.SetTag("container.scale.replicas", replicas);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await inner.ScaleAsync(containerId, replicas, cancellationToken);
            RecordSuccess(OrchestratorActivitySource.ContainerScale, startTimestamp);
        }
        catch (Exception ex)
        {
            RecordError(OrchestratorActivitySource.ContainerScale, startTimestamp, activity, ex);
            throw;
        }
    }

    private static void RecordSuccess(string operation, long startTimestamp) =>
        MetricsHelper.RecordSuccess(operation, startTimestamp,
            OrchestratorMetrics.ContainerOperationCount, OrchestratorMetrics.ContainerOperationDuration);

    private static void RecordError(string operation, long startTimestamp, Activity? activity, Exception ex) =>
        MetricsHelper.RecordError(operation, startTimestamp, activity, ex,
            OrchestratorMetrics.ContainerOperationCount, OrchestratorMetrics.ContainerOperationDuration);
}
