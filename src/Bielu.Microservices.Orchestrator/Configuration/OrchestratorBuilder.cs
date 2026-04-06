using Microsoft.Extensions.DependencyInjection;

namespace Bielu.Microservices.Orchestrator.Configuration;

/// <summary>
/// Builder for configuring the microservices orchestrator.
/// </summary>
public class OrchestratorBuilder
{
    /// <summary>
    /// Gets the service collection.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Gets the orchestrator options that can be modified during configuration.
    /// </summary>
    public OrchestratorOptions Options { get; }

    /// <summary>
    /// Creates a new instance of <see cref="OrchestratorBuilder"/>.
    /// </summary>
    public OrchestratorBuilder(IServiceCollection services, OrchestratorOptions options)
    {
        Services = services;
        Options = options;
    }
}
