using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Storage.File.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bielu.Microservices.Orchestrator.Storage.File.Extensions;

/// <summary>
/// Extension methods for registering the file-based instance store.
/// </summary>
public static class FileInstanceStoreBuilderExtensions
{
    /// <summary>
    /// Registers the file-based instance store for persisting instance state as JSON on disk.
    /// </summary>
    /// <param name="builder">The orchestrator builder.</param>
    /// <param name="configure">Optional delegate to configure <see cref="FileInstanceStoreOptions"/>.</param>
    /// <returns>The builder for chaining.</returns>
    public static OrchestratorBuilder UseFileInstanceStore(
        this OrchestratorBuilder builder,
        Action<FileInstanceStoreOptions>? configure = null)
    {
        var options = new FileInstanceStoreOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);

        // Replace any previously registered store
        builder.Services.RemoveAll<IInstanceStore>();
        builder.Services.AddSingleton<IInstanceStore, FileBasedInstanceStore>();

        return builder;
    }
}
