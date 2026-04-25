using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bielu.Microservices.Orchestrator.Storage.EfCore.Extensions;

/// <summary>
/// Extension methods for registering the EF Core-based instance store.
/// </summary>
public static class EfCoreInstanceStoreBuilderExtensions
{
    /// <summary>
    /// Registers the EF Core-based instance store. Configure the underlying database
    /// provider via the <paramref name="configureDbContext"/> delegate.
    /// </summary>
    /// <param name="builder">The orchestrator builder.</param>
    /// <param name="configureDbContext">
    /// Delegate to configure the <see cref="InstanceStoreDbContext"/> options,
    /// e.g. <c>options.UseSqlServer(connectionString)</c> or <c>options.UseSqlite(connectionString)</c>.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddMicroservicesOrchestrator(builder =>
    /// {
    ///     builder.AddDocker();
    ///     builder.UseEfCoreInstanceStore(options =>
    ///         options.UseSqlite("Data Source=orchestrator.db"));
    ///     builder.WithStateTracking();
    /// });
    /// </code>
    /// </example>
    public static OrchestratorBuilder UseEfCoreInstanceStore(
        this OrchestratorBuilder builder,
        Action<DbContextOptionsBuilder> configureDbContext)
    {
        ArgumentNullException.ThrowIfNull(configureDbContext);
        builder.Services.AddDbContextFactory<InstanceStoreDbContext>(configureDbContext);
        // Replace any previously registered store
        builder.Services.RemoveAll<IInstanceStore>();
        builder.Services.AddSingleton<IInstanceStore, EfCoreInstanceStore>();

        return builder;
    }
}
