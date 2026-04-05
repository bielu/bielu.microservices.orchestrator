using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Docker.Extensions;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.HealthChecks.Extensions;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.OpenTelemetry.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// -------------------------------------------------------------------------
// 1. Orchestrator — register Docker provider + decorate with tracing
// -------------------------------------------------------------------------
// The Docker provider manages containers, images, networks, and volumes
// via the Docker socket. Swap AddDocker() for AddPodman() / AddKubernetes()
// to target a different runtime with zero changes to the rest of the code.
//
// AddOpenTelemetryInstrumentation() wraps every manager interface with a
// thin decorator that opens an Activity span around each operation so that
// any configured OpenTelemetry exporter receives the telemetry.
builder.Services.AddMicroservicesOrchestrator(orchestrator =>
{
    orchestrator
        .AddDocker(options =>
        {
            // Default: unix:///var/run/docker.sock on Linux/Mac,
            //          npipe://./pipe/docker_engine on Windows.
            // Override via DOCKER_HOST env var or explicit assignment:
            //   options.Endpoint = "tcp://remote-host:2376";
        })
        .AddOpenTelemetryInstrumentation(); // must come after the provider
});

// -------------------------------------------------------------------------
// 2. OpenTelemetry — export container-operation traces
// -------------------------------------------------------------------------
// AddOrchestratorInstrumentation() registers the library's ActivitySource
// with the TracerProvider so all spans from the tracing decorators are
// captured and forwarded to the configured exporters.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("orchestrator-api"))
    .WithTracing(tracing => tracing
        .AddOrchestratorInstrumentation()   // <-- orchestrator spans
        .AddAspNetCoreInstrumentation()     // <-- HTTP request spans
        .AddConsoleExporter());             // replace with OTLP in production

// -------------------------------------------------------------------------
// 3. Health checks — verify the container runtime is reachable
// -------------------------------------------------------------------------
// AddContainerRuntimeHealthCheck() registers a check that calls
// IContainerOrchestrator.IsAvailableAsync() and reports Healthy/Unhealthy.
// Tagged with "ready" and "runtime" so the /health/ready endpoint can
// filter on them independently from the liveness check.
builder.Services.AddHealthChecks()
    .AddContainerRuntimeHealthCheck(
        name: "container-runtime",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready", "runtime"]);

var app = builder.Build();

app.MapDefaultEndpoints();

// Liveness  — always reports the process is alive
// Readiness — reports healthy only when the Docker runtime is reachable
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready")
});

// -------------------------------------------------------------------------
// Container lifecycle endpoints
// -------------------------------------------------------------------------

// List all containers (running + stopped when all=true)
app.MapGet("/api/containers", async (IContainerOrchestrator orchestrator, bool all = false) =>
    Results.Ok(await orchestrator.Containers.ListAsync(all)));

// Inspect a single container
app.MapGet("/api/containers/{id}", async (string id, IContainerOrchestrator orchestrator) =>
{
    var container = await orchestrator.Containers.GetAsync(id);
    return container is not null ? Results.Ok(container) : Results.NotFound();
});

// Create a container (does NOT start it automatically)
//
// Example body:
// {
//   "image": "nginx:latest",
//   "name": "my-nginx",
//   "ports": [{ "containerPort": 80, "hostPort": 8080 }],
//   "environmentVariables": { "ENV": "demo" },
//   "labels": { "app": "demo" }
// }
app.MapPost("/api/containers", async (CreateContainerRequest request, IContainerOrchestrator orchestrator) =>
{
    var containerId = await orchestrator.Containers.CreateAsync(request);
    return Results.Created($"/api/containers/{containerId}", new { Id = containerId });
});

// Start a previously created (or stopped) container
app.MapPost("/api/containers/{id}/start", async (string id, IContainerOrchestrator orchestrator) =>
{
    await orchestrator.Containers.StartAsync(id);
    return Results.Ok(new { Message = $"Container {id} started." });
});

// Stop a running container
// Optional query parameter ?timeoutSeconds=30 controls graceful-stop window
app.MapPost("/api/containers/{id}/stop", async (
    string id,
    IContainerOrchestrator orchestrator,
    int? timeoutSeconds = null) =>
{
    var timeout = timeoutSeconds.HasValue ? TimeSpan.FromSeconds(timeoutSeconds.Value) : (TimeSpan?)null;
    await orchestrator.Containers.StopAsync(id, timeout);
    return Results.Ok(new { Message = $"Container {id} stopped." });
});

// Tail logs from a container (stdout + stderr)
app.MapGet("/api/containers/{id}/logs", async (string id, IContainerOrchestrator orchestrator) =>
{
    var logs = await orchestrator.Containers.GetLogsAsync(id);
    return Results.Ok(new { Logs = logs });
});

// Remove a container (use ?force=true to remove a running container)
app.MapDelete("/api/containers/{id}", async (string id, IContainerOrchestrator orchestrator, bool force = false) =>
{
    await orchestrator.Containers.RemoveAsync(id, force);
    return Results.NoContent();
});

// -------------------------------------------------------------------------
// Image endpoints
// -------------------------------------------------------------------------
app.MapGet("/api/images", async (IContainerOrchestrator orchestrator) =>
    Results.Ok(await orchestrator.Images.ListAsync()));

// Pull an image — body: { "image": "nginx", "tag": "latest" }
app.MapPost("/api/images/pull", async (PullImageRequest request, IContainerOrchestrator orchestrator) =>
{
    await orchestrator.Images.PullAsync(request);
    return Results.Ok(new { Message = $"Pulled {request.Image}:{request.Tag}" });
});

app.MapDelete("/api/images/{id}", async (string id, IContainerOrchestrator orchestrator, bool force = false) =>
{
    await orchestrator.Images.RemoveAsync(id, force);
    return Results.NoContent();
});

// -------------------------------------------------------------------------
// Network & volume endpoints
// -------------------------------------------------------------------------
app.MapGet("/api/networks", async (IContainerOrchestrator orchestrator) =>
    Results.Ok(await orchestrator.Networks.ListAsync()));

app.MapGet("/api/volumes", async (IContainerOrchestrator orchestrator) =>
    Results.Ok(await orchestrator.Volumes.ListAsync()));

// -------------------------------------------------------------------------
// Provider info
// -------------------------------------------------------------------------
app.MapGet("/api/provider", async (IContainerOrchestrator orchestrator) =>
{
    var available = await orchestrator.IsAvailableAsync();
    return Results.Ok(new { orchestrator.ProviderName, IsAvailable = available });
});

app.Run();
