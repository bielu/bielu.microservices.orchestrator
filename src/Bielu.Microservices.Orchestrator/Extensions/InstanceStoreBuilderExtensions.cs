using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bielu.Microservices.Orchestrator.Extensions;

/// <summary>
/// Extension methods for configuring instance state storage on the <see cref="OrchestratorBuilder"/>.
/// </summary>
public static class InstanceStoreBuilderExtensions
{
    /// <summary>
    /// Registers the in-memory instance store. This is the default store
    /// and does not persist state across orchestrator restarts.
    /// </summary>
    /// <param name="builder">The orchestrator builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static OrchestratorBuilder UseInMemoryInstanceStore(this OrchestratorBuilder builder)
    {
        builder.Services.RemoveAll<IInstanceStore>();
        builder.Services.AddSingleton<IInstanceStore, InMemoryInstanceStore>();
        return builder;
    }

    /// <summary>
    /// Enables state tracking so that create, remove, and scale operations are
    /// automatically persisted to the registered <see cref="IInstanceStore"/>.
    /// Also registers the <see cref="InstanceRecoveryService"/> hosted service
    /// for startup reconciliation.
    /// </summary>
    /// <param name="builder">The orchestrator builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static OrchestratorBuilder WithStateTracking(this OrchestratorBuilder builder)
    {
        builder.Services.Decorate<IContainerManager, StateTrackingContainerManagerDecorator>();
        builder.Services.AddHostedService<InstanceRecoveryService>();
        return builder;
    }
}
