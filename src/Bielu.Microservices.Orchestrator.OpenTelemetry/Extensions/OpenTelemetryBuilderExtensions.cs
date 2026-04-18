using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.OpenTelemetry.Instrumentation;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Bielu.Microservices.Orchestrator.OpenTelemetry.Extensions;

/// <summary>
/// Extension methods for adding OpenTelemetry instrumentation to the orchestrator.
/// </summary>
public static class OpenTelemetryBuilderExtensions
{
    /// <summary>
    /// Deferred decorator priority for OpenTelemetry instrumentation.
    /// Applied as the outermost wrapper (high value = outer).
    /// </summary>
    public const int DecoratorPriority = 900;

    /// <summary>
    /// Adds OpenTelemetry tracing and metrics decorators around all registered manager interfaces,
    /// and registers the <see cref="OrchestratorMetricsCollector"/> hosted service that periodically
    /// collects gauge-style metrics (managed instance counts, host CPU/RAM, etc.).
    /// Call this after registering a runtime provider (e.g., <c>AddDocker()</c>).
    /// <para>
    /// The OTel decorators are applied at <see cref="DecoratorPriority"/>
    /// priority, ensuring they are always the outermost wrappers in the call chain
    /// regardless of registration order relative to state tracking or other decorators.
    /// </para>
    /// </summary>
    /// <param name="builder">The orchestrator builder.</param>
    /// <returns>The orchestrator builder for chaining.</returns>
    public static OrchestratorBuilder AddOpenTelemetryInstrumentation(this OrchestratorBuilder builder)
    {
        builder.AddDeferredDecorator(
            DecoratorPriority,
            services =>
            {
                services.Decorate<IContainerManager, OpenTelemetryContainerManagerDecorator>();
                services.Decorate<IImageManager, OpenTelemetryImageManagerDecorator>();
                services.Decorate<INetworkManager, OpenTelemetryNetworkManagerDecorator>();
                services.Decorate<IVolumeManager, OpenTelemetryVolumeManagerDecorator>();
            });

        builder.Services.AddHostedService<OrchestratorMetricsCollector>();

        return builder;
    }

    /// <summary>
    /// Registers the orchestrator <see cref="System.Diagnostics.ActivitySource"/> with the
    /// OpenTelemetry <see cref="TracerProviderBuilder"/> so spans are exported.
    /// </summary>
    /// <param name="builder">The tracer provider builder.</param>
    /// <returns>The tracer provider builder for chaining.</returns>
    public static TracerProviderBuilder AddOrchestratorInstrumentation(this TracerProviderBuilder builder)
    {
        return builder.AddSource(OrchestratorActivitySource.Name);
    }

    /// <summary>
    /// Registers the orchestrator <see cref="System.Diagnostics.Metrics.Meter"/> with the
    /// OpenTelemetry <see cref="MeterProviderBuilder"/> so metrics are exported.
    /// </summary>
    /// <param name="builder">The meter provider builder.</param>
    /// <returns>The meter provider builder for chaining.</returns>
    public static MeterProviderBuilder AddOrchestratorMetrics(this MeterProviderBuilder builder)
    {
        return builder.AddMeter(OrchestratorMetrics.Name);
    }
}
