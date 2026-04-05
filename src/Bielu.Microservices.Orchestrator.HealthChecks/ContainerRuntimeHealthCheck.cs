using Bielu.Microservices.Orchestrator.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bielu.Microservices.Orchestrator.HealthChecks;

/// <summary>
/// A health check that verifies a container runtime is reachable via
/// <see cref="IContainerOrchestrator.IsAvailableAsync"/>.
/// </summary>
/// <param name="orchestrator">The orchestrator to check.</param>
public class ContainerRuntimeHealthCheck(IContainerOrchestrator orchestrator) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isAvailable = await orchestrator.IsAvailableAsync(cancellationToken);

            return isAvailable
                ? HealthCheckResult.Healthy(
                    $"Container runtime '{orchestrator.ProviderName}' is available.",
                    data: new Dictionary<string, object> { ["provider"] = orchestrator.ProviderName })
                : HealthCheckResult.Unhealthy(
                    $"Container runtime '{orchestrator.ProviderName}' is not reachable.",
                    data: new Dictionary<string, object> { ["provider"] = orchestrator.ProviderName });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"Container runtime '{orchestrator.ProviderName}' health check threw an exception.",
                exception: ex,
                data: new Dictionary<string, object> { ["provider"] = orchestrator.ProviderName });
        }
    }
}
