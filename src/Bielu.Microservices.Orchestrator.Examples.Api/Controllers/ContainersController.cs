using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Microsoft.AspNetCore.Mvc;

namespace Bielu.Microservices.Orchestrator.Examples.Api.Controllers;

/// <summary>
/// Container lifecycle endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ContainersController(IContainerOrchestrator orchestrator) : ControllerBase
{
    /// <summary>
    /// List all containers (running + stopped when all=true).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(bool all = false) =>
        Ok(await orchestrator.Containers.ListAsync(all));

    /// <summary>
    /// Inspect a single container.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var container = await orchestrator.Containers.GetAsync(id);
        return container is not null ? Ok(container) : NotFound();
    }

    /// <summary>
    /// Create a container (does NOT start it automatically).
    /// </summary>
    /// <remarks>
    /// Example body:
    /// <code>
    /// {
    ///   "image": "nginx:latest",
    ///   "name": "my-nginx",
    ///   "ports": [{ "containerPort": 80, "hostPort": 8080 }],
    ///   "environmentVariables": { "ENV": "demo" },
    ///   "labels": { "app": "demo" },
    ///   "replicas": 3
    /// }
    /// </code>
    /// </remarks>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateContainerRequest request)
    {
        var containerId = await orchestrator.Containers.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = containerId }, new { Id = containerId });
    }

    /// <summary>
    /// Start a previously created (or stopped) container.
    /// </summary>
    [HttpPost("{id}/start")]
    public async Task<IActionResult> Start(string id)
    {
        await orchestrator.Containers.StartAsync(id);
        return Ok(new { Message = $"Container {id} started." });
    }

    /// <summary>
    /// Stop a running container.
    /// Optional query parameter ?timeoutSeconds=30 controls graceful-stop window.
    /// </summary>
    [HttpPost("{id}/stop")]
    public async Task<IActionResult> Stop(string id, int? timeoutSeconds = null)
    {
        var timeout = timeoutSeconds.HasValue ? TimeSpan.FromSeconds(timeoutSeconds.Value) : (TimeSpan?)null;
        await orchestrator.Containers.StopAsync(id, timeout);
        return Ok(new { Message = $"Container {id} stopped." });
    }

    /// <summary>
    /// Tail logs from a container (stdout + stderr).
    /// </summary>
    [HttpGet("{id}/logs")]
    public async Task<IActionResult> Logs(string id)
    {
        var logs = await orchestrator.Containers.GetLogsAsync(id);
        return Ok(new { Logs = logs });
    }

    /// <summary>
    /// Remove a container (use ?force=true to remove a running container).
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Remove(string id, bool force = false)
    {
        await orchestrator.Containers.RemoveAsync(id, force);
        return NoContent();
    }

    /// <summary>
    /// Scale the number of instances for a container (runtime-dependent).
    /// </summary>
    [HttpPost("{id}/scale")]
    public async Task<IActionResult> Scale(string id, int replicas)
    {
        await orchestrator.Containers.ScaleAsync(id, replicas);
        return Ok(new { Message = $"Container {id} scaled to {replicas} replicas." });
    }

    /// <summary>
    /// Demo: start a worker container attached to the Aspire session network so it
    /// can resolve other Aspire-managed containers (e.g. postgres, kafka) by name.
    /// </summary>
    /// <remarks>
    /// Aspire creates a dedicated bridge network named
    /// <c>aspire-session-network-{sessionId}-{appHostName}</c> for every run.
    /// We discover it at runtime and pass it in <see cref="CreateContainerRequest.Networks"/>
    /// so the orchestrator connects the new container to the same network after creation.
    ///
    /// Example: POST /api/containers/aspire-worker?image=example-worker:latest
    /// </remarks>
    [HttpPost("aspire-worker")]
    public async Task<IActionResult> StartWorkerOnAspireNetwork(
        [FromQuery] string image = "example-worker:latest",
        [FromQuery] string name = "example-worker")
    {
        // 1) Find the Aspire session network created by the AppHost for this run.
        var networks = await orchestrator.Networks.ListAsync();
        var aspireNetwork = networks
            .FirstOrDefault(n => n.Name.StartsWith("aspire-session-network-",
                StringComparison.OrdinalIgnoreCase));

        if (aspireNetwork is null)
        {
            return Problem(
                title: "Aspire session network not found",
                detail: "No docker network with prefix 'aspire-session-network-' was found. " +
                        "Make sure the API is running under the Aspire AppHost.",
                statusCode: StatusCodes.Status409Conflict);
        }

        // 2) Create the container on that network so it shares DNS with
        //    other Aspire resources (postgres, kafka, otel-collector, ...).
        var request = new CreateContainerRequest
        {
            Name = name,
            Image = image,
            EnvironmentVariables =
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://opentelemetry-collector:4317",
                ["OTEL_SERVICE_NAME"] = name,
                // Aspire registers each resource as a DNS alias on the session network,
                // so connection strings like "Host=postgres" or "kafka:9092" just work.
                ["ConnectionStrings__orchestratordb"] =
                    "Host=postgres;Port=5432;Database=orchestratordb;Username=postgres;Password=postgres"
            },
            Labels =
            {
                ["app"] = name,
                ["started-by"] = "ContainersController"
            },
            Networks =
            {
                new NetworkAttachment
                {
                    NetworkName = aspireNetwork.Name,
                    Aliases = { name }
                }
            },
            AutoRemove = true
        };

        var containerId = await orchestrator.Containers.CreateAsync(request);
        await orchestrator.Containers.StartAsync(containerId);

        return Ok(new
        {
            ContainerId = containerId,
            Network = aspireNetwork.Name,
            Message = $"Started '{name}' on Aspire network '{aspireNetwork.Name}'."
        });
    }
}
