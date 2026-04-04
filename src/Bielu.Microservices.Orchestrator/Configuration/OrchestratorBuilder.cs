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
    /// Creates a new instance of <see cref="OrchestratorBuilder"/>.
    /// </summary>
    public OrchestratorBuilder(IServiceCollection services)
    {
        Services = services;
    }
}
