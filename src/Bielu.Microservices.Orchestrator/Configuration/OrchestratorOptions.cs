namespace Bielu.Microservices.Orchestrator.Configuration;

/// <summary>
/// Configuration options for the microservices orchestrator.
/// </summary>
public class OrchestratorOptions
{
    /// <summary>
    /// The default provider to use when multiple are registered.
    /// </summary>
    public string? DefaultProvider { get; set; }

    /// <summary>
    /// When <c>true</c>, the orchestrator only lists and manages containers that it
    /// created (identified by the <see cref="OrchestratorLabels.ManagedBy"/> label).
    /// When <c>false</c>, all containers on the host are visible.
    /// </summary>
    public bool ManagedContainersOnly { get; set; } = true;

    /// <summary>
    /// Stable identifier of this orchestrator instance, persisted with every
    /// <see cref="Models.ManagedInstance"/> so that, after a restart, the orchestrator
    /// can identify which records it owns. Defaults to a process-stable GUID; for
    /// multi-host deployments this should be configured explicitly (e.g. from machine
    /// identity or configuration) so it survives restarts.
    /// </summary>
    public Guid OrchestratorId { get; set; } = Guid.NewGuid();
}
