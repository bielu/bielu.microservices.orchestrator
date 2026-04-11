using Microsoft.Extensions.DependencyInjection;

namespace Bielu.Microservices.Orchestrator.Configuration;

/// <summary>
/// Builder for configuring the microservices orchestrator.
/// </summary>
public class OrchestratorBuilder
{
    private readonly SortedList<int, List<Action<IServiceCollection>>> _deferredDecorators = new();

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

    /// <summary>
    /// Queues a decorator registration to be applied after all builder configuration
    /// has completed. Decorators are applied in ascending <paramref name="priority"/>
    /// order so that lower-priority decorators wrap closer to the provider and
    /// higher-priority decorators wrap outermost.
    /// <para>
    /// Each decorator package defines its own priority constant (e.g.
    /// <c>StateTrackingContainerManagerDecorator.DecoratorPriority</c> = 100,
    /// <c>OpenTelemetryBuilderExtensions.DecoratorPriority</c> = 900).
    /// </para>
    /// </summary>
    /// <param name="priority">
    /// The execution priority. Lower values are applied first (innermost).
    /// </param>
    /// <param name="registration">
    /// An action that calls <c>services.Decorate&lt;T, TDecorator&gt;()</c>.
    /// </param>
    public void AddDeferredDecorator(int priority, Action<IServiceCollection> registration)
    {
        if (!_deferredDecorators.TryGetValue(priority, out var list))
        {
            list = [];
            _deferredDecorators[priority] = list;
        }

        list.Add(registration);
    }

    /// <summary>
    /// Applies all queued decorator registrations in deterministic priority order.
    /// Called once by the framework after the user's configure delegate has finished.
    /// </summary>
    internal void ApplyDeferredDecorators()
    {
        foreach (var (_, registrations) in _deferredDecorators)
        {
            foreach (var registration in registrations)
            {
                registration(Services);
            }
        }
    }
}
