using Bielu.Microservices.Orchestrator.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bielu.Microservices.Orchestrator.HealthChecks;

/// <summary>
/// A health check that verifies a container runtime is reachable via
/// <see cref="IContainerOrchestrator.IsAvailableAsync"/>.
/// </summary>
public class ContainerRuntimeHealthCheck : IHealthCheck
{
    private readonly IContainerOrchestrator _orchestrator;

    /// <summary>
    /// Creates a new instance of <see cref="ContainerRuntimeHealthCheck"/>.
    /// </summary>
    /// <param name="orchestrator">The orchestrator to check.</param>
    public ContainerRuntimeHealthCheck(IContainerOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isAvailable = await _orchestrator.IsAvailableAsync(cancellationToken);

            return isAvailable
                ? HealthCheckResult.Healthy(
                    $"Container runtime '{_orchestrator.ProviderName}' is available.",
                    data: new Dictionary<string, object> { ["provider"] = _orchestrator.ProviderName })
                : HealthCheckResult.Unhealthy(
                    $"Container runtime '{_orchestrator.ProviderName}' is not reachable.",
                    data: new Dictionary<string, object> { ["provider"] = _orchestrator.ProviderName });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"Container runtime '{_orchestrator.ProviderName}' health check threw an exception.",
                exception: ex,
                data: new Dictionary<string, object> { ["provider"] = _orchestrator.ProviderName });
        }
    }
}
