using Bielu.Microservices.Orchestrator.Docker.Extensions;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.HealthChecks.Extensions;
using Bielu.Microservices.Orchestrator.OpenTelemetry.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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
// thin decorator that opens an Activity span and records metrics around
// each operation so that any configured OpenTelemetry exporter receives
// the telemetry.
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
// 2. Health checks — verify the container runtime is reachable
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

// -------------------------------------------------------------------------
// 3. Controllers — register MVC controllers for API endpoints
// -------------------------------------------------------------------------
builder.Services.AddControllers();

var app = builder.Build();

app.MapOpenApi();

app.MapDefaultEndpoints();

// Liveness  — always reports the process is alive
// Readiness — reports healthy only when the Docker runtime is reachable
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready")
});

app.MapControllers();

app.Run();
