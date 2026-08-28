using Bielu.Microservices.Orchestrator.Gateway.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bielu.Microservices.Orchestrator.Gateway.Services;

/// <summary>
/// Background service that periodically removes registered instances whose TTL has expired.
/// </summary>
public sealed class RegistrationCleanupService(
    OrchestratorRegistrationStore store,
    GatewayOptions options,
    ILogger<RegistrationCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Registration cleanup service started. TTL={TtlSeconds}s", options.InstanceTtlSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(CleanupInterval, stoppingToken);

            var removed = store.RemoveExpired();
            if (removed > 0)
            {
                logger.LogInformation("Cleaned up {Count} expired instance(s)", removed);
            }
        }
    }
}
