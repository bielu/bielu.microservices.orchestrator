using Bielu.Microservices.Orchestrator;
using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.Examples.Api.Services;

/// <summary>
/// A background service that automatically starts a worker container on application startup.
/// This demonstrates how to use the orchestrator to ensure dependent infrastructure is running.
/// </summary>
public class WorkerStartupService(IContainerOrchestrator orchestrator, ILogger<WorkerStartupService> logger) : IHostedService
{
    private const string WorkerName = "startup-worker";
    private const string WorkerImage = "example-worker:d515ca66819511632e14eddb765c8f130bcea01c";

    // Label key we stamp on the worker so we can tell which Aspire network it was started on
    // and recreate it when Aspire spins a fresh network on the next AppHost run.
    private const string NetworkLabel = "bielu.orchestrator.aspire-network";
    private const string StartedByLabel = "started-by";
    private const string StartedByValue = "WorkerStartupService";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("WorkerStartupService is starting...");

        // Run on a background task so we don't block app startup (and therefore the
        // host's network/health reporting) while we wait for the Aspire session
        // network to appear. Failures are logged but never surfaced to the host.
        _ = Task.Run(() => RunAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 1) Find the Aspire network (session OR persistent — Aspire 9.4+/13).
            //    - Session:   aspire-session-network-<hash>-<AppHost>     (recreated every run)
            //    - Persistent: aspire-persistent-network-<hash>-<AppHost> (reused across runs;
            //      used when any container resource has ContainerLifetime.Persistent)
            //    We poll for a short window because DCP may still be creating the network
            //    when this service starts.
            var runningUnderAspire = IsRunningUnderAspire();

            string? networkName = null;
            var maxAttempts = runningUnderAspire ? 30 : 1; // ~30s when under Aspire, otherwise no wait
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                networkName = await TryFindAspireNetworkAsync(cancellationToken);
                if (networkName != null) break;

                if (attempt < maxAttempts)
                {
                    logger.LogDebug(
                        "Aspire network not visible yet (attempt {Attempt}/{Max}); waiting before retry.",
                        attempt, maxAttempts);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }

            if (runningUnderAspire && networkName == null)
            {
                logger.LogWarning(
                    "Running under Aspire but no aspire-(session|persistent)-network-* became visible after {Max}s; " +
                    "starting worker on the default network instead.", maxAttempts);
            }

            // 2) If a worker we previously created already exists, decide whether to keep it
            //    or recreate it on the (possibly new) Aspire network.
            var containers = await orchestrator.Containers.ListAsync(all: true, cancellationToken);
            var existing = containers.FirstOrDefault(c => c.Name == WorkerName);

            if (existing != null)
            {
                var managedByUs = existing.Labels.TryGetValue(StartedByLabel, out var startedBy)
                                  && string.Equals(startedBy, StartedByValue, StringComparison.Ordinal);
                existing.Labels.TryGetValue(NetworkLabel, out var previousNetwork);

                var sameNetwork = string.Equals(previousNetwork ?? string.Empty,
                    networkName ?? string.Empty, StringComparison.Ordinal);

                if (managedByUs && !sameNetwork)
                {
                    logger.LogInformation(
                        "Existing worker '{Name}' was attached to network '{Previous}', but current Aspire network is '{Current}'. Recreating.",
                        WorkerName, previousNetwork ?? "<none>", networkName ?? "<none>");
                    try
                    {
                        await orchestrator.Containers.RemoveAsync(existing.Id, force: true, cancellationToken: cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Failed to remove stale worker container {Id}; will attempt to continue.", existing.Id);
                        return;
                    }
                }
                else
                {
                    logger.LogInformation(
                        "Worker container '{Name}' already exists on the expected network. Skipping creation.",
                        WorkerName);
                    return;
                }
            }

            // 3) Look up the image digest so we can stamp it on the container and
            //    later detect when a newer image is available via IImageUpdateService.
            var localImage = (await orchestrator.Images.ListAsync(cancellationToken))
                .FirstOrDefault(i => i.Tags.Any(t =>
                    string.Equals(t, WorkerImage, StringComparison.OrdinalIgnoreCase)));
            var imageDigest = localImage?.Id ?? string.Empty;

            // 4) Define and create the worker container.
            var request = new CreateContainerRequest
            {
                Name = WorkerName,
                Image = WorkerImage,
                EnvironmentVariables =
                {
                    ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://opentelemetry-collector:4317",
                    ["OTEL_SERVICE_NAME"] = WorkerName,
                    ["ConnectionStrings__orchestratordb"] =
                        "Host=postgres;Port=5432;Database=orchestratordb;Username=postgres;Password=postgres"
                },
                Labels =
                {
                    ["app"] = WorkerName,
                    [StartedByLabel] = StartedByValue,
                    [NetworkLabel] = networkName ?? string.Empty,
                    [OrchestratorLabels.Image] = WorkerImage,
                    [OrchestratorLabels.ImageDigest] = imageDigest,
                    [OrchestratorLabels.InstanceId] = WorkerName
                },
                AutoRemove = true
            };

            if (networkName != null)
            {
                request.Networks.Add(new NetworkAttachment
                {
                    NetworkName = networkName,
                    Aliases = { WorkerName }
                });
                logger.LogInformation("Attaching startup worker to Aspire network: {Network}", networkName);
            }
            else
            {
                logger.LogInformation("No Aspire network found, using default network for startup worker.");
            }

            var containerId = await orchestrator.Containers.CreateAsync(request, cancellationToken);
            await orchestrator.Containers.StartAsync(containerId, cancellationToken);

            logger.LogInformation("Successfully started worker container {ContainerId} on startup.", containerId);
        }
        catch (Exception ex)
        {
            // We don't want to crash the app if the worker fails to start
            logger.LogError(ex, "Failed to start worker container on startup.");
        }
    }

    private static bool IsRunningUnderAspire()
    {
        return Environment.GetEnvironmentVariables()
                   .Keys.Cast<string>()
                   .Any(k => k.StartsWith("ASPIRE_", StringComparison.OrdinalIgnoreCase))
               || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL"));
    }

    /// <summary>
    /// Returns the name of the current Aspire-managed Docker network, preferring the
    /// persistent variant introduced in Aspire 9.4+ over the per-session one. Returns
    /// <c>null</c> when no such network exists yet.
    /// </summary>
    private async Task<string?> TryFindAspireNetworkAsync(CancellationToken cancellationToken)
    {
        var networks = await orchestrator.Networks.ListAsync(cancellationToken);

        var persistent = networks.FirstOrDefault(n =>
            n.Name.StartsWith("aspire-persistent-network-", StringComparison.OrdinalIgnoreCase));
        if (persistent != null) return persistent.Name;

        var session = networks.FirstOrDefault(n =>
            n.Name.StartsWith("aspire-session-network-", StringComparison.OrdinalIgnoreCase));
        return session?.Name;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
