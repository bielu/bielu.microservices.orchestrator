using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bielu.Microservices.Orchestrator.HealthChecks.Extensions;

/// <summary>
/// Extension methods for registering container runtime health checks.
/// </summary>
public static class HealthChecksBuilderExtensions
{
    /// <summary>
    /// Adds a health check that verifies the registered container runtime is reachable.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The name of the health check. Defaults to "container-runtime".</param>
    /// <param name="failureStatus">
    /// The <see cref="HealthStatus"/> that should be reported when the health check fails.
    /// Defaults to <see cref="HealthStatus.Unhealthy"/>.
    /// </param>
    /// <param name="tags">
    /// Optional tags used to filter health check results (e.g., "ready", "runtime").
    /// </param>
    /// <returns>The health checks builder for chaining.</returns>
    public static IHealthChecksBuilder AddContainerRuntimeHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "container-runtime",
        HealthStatus failureStatus = HealthStatus.Unhealthy,
        IEnumerable<string>? tags = null)
    {
        return builder.AddCheck<ContainerRuntimeHealthCheck>(
            name,
            failureStatus,
            tags ?? ["ready", "runtime"]);
    }
}
