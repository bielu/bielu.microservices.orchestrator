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
}
