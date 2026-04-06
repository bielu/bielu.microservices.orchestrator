using Bielu.Microservices.Orchestrator.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bielu.Microservices.Orchestrator.Extensions;

/// <summary>
/// Extension methods for registering the microservices orchestrator.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the microservices orchestrator to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">A delegate to configure the <see cref="OrchestratorBuilder"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMicroservicesOrchestrator(
        this IServiceCollection services,
        Action<OrchestratorBuilder>? configure = null)
    {
        var options = new OrchestratorOptions();
        var builder = new OrchestratorBuilder(services, options);
        configure?.Invoke(builder);

        services.AddSingleton(options);

        return services;
    }
}
