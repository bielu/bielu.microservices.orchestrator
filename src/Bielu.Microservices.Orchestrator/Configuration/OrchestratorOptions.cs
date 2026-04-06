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
}
