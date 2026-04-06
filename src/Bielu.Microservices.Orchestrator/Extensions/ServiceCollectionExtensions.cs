using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // Register the default in-memory instance store if no store was registered by the builder
        services.TryAddSingleton<IInstanceStore, InMemoryInstanceStore>();

        // Apply deferred decorators in deterministic priority order so that
        // registration ordering in the configure delegate does not matter.
        // E.g. state tracking is always inner, OTel is always outermost.
        builder.ApplyDeferredDecorators();

        return services;
    }
}
