using Bielu.Microservices.Orchestrator.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Bielu.Microservices.Orchestrator.Examples.Api.Controllers;

/// <summary>
/// Volume management endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class VolumesController(IContainerOrchestrator orchestrator) : ControllerBase
{
    /// <summary>
    /// List all volumes.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await orchestrator.Volumes.ListAsync());

    /// <summary>
    /// Get a volume by name.
    /// </summary>
    [HttpGet("{name}")]
    public async Task<IActionResult> Get(string name)
    {
        var volume = await orchestrator.Volumes.GetAsync(name);
        return volume is null ? NotFound() : Ok(volume);
    }
}
