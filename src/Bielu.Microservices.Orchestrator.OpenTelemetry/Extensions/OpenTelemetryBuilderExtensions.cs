using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.OpenTelemetry.Instrumentation;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace Bielu.Microservices.Orchestrator.OpenTelemetry.Extensions;

/// <summary>
/// Extension methods for adding OpenTelemetry instrumentation to the orchestrator.
/// </summary>
public static class OpenTelemetryBuilderExtensions
{
    /// <summary>
    /// Adds OpenTelemetry tracing decorators around all registered manager interfaces.
    /// Call this after registering a runtime provider (e.g., <c>AddDocker()</c>).
    /// </summary>
    /// <param name="builder">The orchestrator builder.</param>
    /// <returns>The orchestrator builder for chaining.</returns>
    public static OrchestratorBuilder AddOpenTelemetryInstrumentation(this OrchestratorBuilder builder)
    {
        builder.Services.Decorate<IContainerManager, TracedContainerManager>();
        builder.Services.Decorate<IImageManager, TracedImageManager>();
        builder.Services.Decorate<INetworkManager, TracedNetworkManager>();
        builder.Services.Decorate<IVolumeManager, TracedVolumeManager>();

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
}
