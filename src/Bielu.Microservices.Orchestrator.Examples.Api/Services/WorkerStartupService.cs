using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.Examples.Api.Services;

/// <summary>
/// A background service that automatically starts a worker container on application startup.
/// This demonstrates how to use the orchestrator to ensure dependent infrastructure is running.
/// </summary>
public class WorkerStartupService(IContainerOrchestrator orchestrator, ILogger<WorkerStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("WorkerStartupService is starting...");

        try
        {
            // 1) Find the Aspire session network (if running under Aspire)
            var networks = await orchestrator.Networks.ListAsync(cancellationToken);
            var aspireNetwork = networks
                .FirstOrDefault(n => n.Name.StartsWith("aspire-session-network-",
                    StringComparison.OrdinalIgnoreCase));

            var networkName = aspireNetwork?.Name;
            
            // 2) Define the worker container
            var name = "startup-worker";
            var image = "example-worker:d515ca66819511632e14eddb765c8f130bcea01c";

            // Check if it already exists to avoid conflicts
            var containers = await orchestrator.Containers.ListAsync(all: true, cancellationToken);
            if (containers.Any(c => c.Name == name))
            {
                logger.LogInformation("Worker container '{Name}' already exists. Skipping creation.", name);
                return;
            }

            var request = new CreateContainerRequest
            {
                Name = name,
                Image = image,
                EnvironmentVariables =
                {
                    ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://opentelemetry-collector:4317",
                    ["OTEL_SERVICE_NAME"] = name,
                    ["ConnectionStrings__orchestratordb"] =
                        "Host=postgres;Port=5432;Database=orchestratordb;Username=postgres;Password=postgres"
                },
                Labels =
                {
                    ["app"] = name,
                    ["started-by"] = "WorkerStartupService"
                },
                AutoRemove = true
            };

            if (networkName != null)
            {
                request.Networks.Add(new NetworkAttachment
                {
                    NetworkName = networkName,
                    Aliases = { name }
                });
                logger.LogInformation("Attaching startup worker to Aspire network: {Network}", networkName);
            }
            else
            {
                logger.LogInformation("No Aspire network found, using default network for startup worker.");
            }

            // 3) Create and start the container
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

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
