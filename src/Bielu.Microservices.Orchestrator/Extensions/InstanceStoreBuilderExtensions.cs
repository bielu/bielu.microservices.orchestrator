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
    /// <para>
    /// The state-tracking decorator is applied at
    /// <see cref="StateTrackingContainerManagerDecorator.DecoratorPriority"/>
    /// priority, ensuring it always wraps closest to the provider regardless of
    /// registration order relative to other decorators.
    /// </para>
    /// </summary>
    /// <param name="builder">The orchestrator builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static OrchestratorBuilder WithStateTracking(this OrchestratorBuilder builder)
    {
        builder.AddDeferredDecorator(
            StateTrackingContainerManagerDecorator.DecoratorPriority,
            services => services.Decorate<IContainerManager, StateTrackingContainerManagerDecorator>());

        builder.Services.AddHostedService<InstanceRecoveryService>();
        return builder;
    }
}
