using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Docker.Extensions;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Models;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Register the microservices orchestrator with Docker provider
builder.Services.AddMicroservicesOrchestrator(orchestrator =>
{
    orchestrator.AddDocker(options =>
    {
        // Uses default Docker socket endpoint
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

// Container management endpoints
app.MapGet("/api/containers", async (IContainerOrchestrator orchestrator) =>
{
    var containers = await orchestrator.Containers.ListAsync(all: true);
    return Results.Ok(containers);
});

app.MapGet("/api/containers/{id}", async (string id, IContainerOrchestrator orchestrator) =>
{
    var container = await orchestrator.Containers.GetAsync(id);
    return container is not null ? Results.Ok(container) : Results.NotFound();
});

app.MapPost("/api/containers", async (CreateContainerRequest request, IContainerOrchestrator orchestrator) =>
{
    var containerId = await orchestrator.Containers.CreateAsync(request);
    return Results.Created($"/api/containers/{containerId}", new { Id = containerId });
});

app.MapPost("/api/containers/{id}/start", async (string id, IContainerOrchestrator orchestrator) =>
{
    await orchestrator.Containers.StartAsync(id);
    return Results.Ok();
});

app.MapPost("/api/containers/{id}/stop", async (string id, IContainerOrchestrator orchestrator) =>
{
    await orchestrator.Containers.StopAsync(id);
    return Results.Ok();
});

app.MapDelete("/api/containers/{id}", async (string id, IContainerOrchestrator orchestrator) =>
{
    await orchestrator.Containers.RemoveAsync(id, force: true);
    return Results.NoContent();
});

// Image management endpoints
app.MapGet("/api/images", async (IContainerOrchestrator orchestrator) =>
{
    var images = await orchestrator.Images.ListAsync();
    return Results.Ok(images);
});

app.MapPost("/api/images/pull", async (PullImageRequest request, IContainerOrchestrator orchestrator) =>
{
    await orchestrator.Images.PullAsync(request);
    return Results.Ok();
});

app.MapDelete("/api/images/{id}", async (string id, IContainerOrchestrator orchestrator) =>
{
    await orchestrator.Images.RemoveAsync(id);
    return Results.NoContent();
});

// Network management endpoints
app.MapGet("/api/networks", async (IContainerOrchestrator orchestrator) =>
{
    var networks = await orchestrator.Networks.ListAsync();
    return Results.Ok(networks);
});

// Volume management endpoints
app.MapGet("/api/volumes", async (IContainerOrchestrator orchestrator) =>
{
    var volumes = await orchestrator.Volumes.ListAsync();
    return Results.Ok(volumes);
});

// Provider info endpoint
app.MapGet("/api/provider", async (IContainerOrchestrator orchestrator) =>
{
    var available = await orchestrator.IsAvailableAsync();
    return Results.Ok(new { orchestrator.ProviderName, IsAvailable = available });
});

app.Run();
