using Bielu.Microservices.Orchestrator.Configuration;

namespace Bielu.Microservices.Orchestrator.Extensions;

/// <summary>
/// Fluent API extension methods for <see cref="OrchestratorBuilder"/>.
/// </summary>
public static class OrchestratorBuilderExtensions
{
    /// <summary>
    /// Configures the orchestrator to only list and manage containers it created.
    /// This is the default behavior.
    /// </summary>
    /// <param name="builder">The orchestrator builder.</param>
    /// <param name="managedOnly">
    /// <c>true</c> to restrict to orchestrator-managed containers;
    /// <c>false</c> to show all containers on the host.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    public static OrchestratorBuilder WithManagedContainersOnly(
        this OrchestratorBuilder builder, bool managedOnly = true)
    {
        builder.Options.ManagedContainersOnly = managedOnly;
        return builder;
    }

    /// <summary>
    /// Sets the default runtime provider name (e.g. "Docker", "Containerd").
    /// </summary>
    /// <param name="builder">The orchestrator builder.</param>
    /// <param name="providerName">The provider name. Must not be null or empty.</param>
    /// <returns>The builder for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="providerName"/> is null or empty.</exception>
    public static OrchestratorBuilder WithDefaultProvider(
        this OrchestratorBuilder builder, string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        builder.Options.DefaultProvider = providerName;
        return builder;
    }
}
