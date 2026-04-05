using Bielu.Microservices.Orchestrator.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Bielu.Microservices.Orchestrator.Examples.Api.Controllers;

/// <summary>
/// Provider information endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ProviderController(IContainerOrchestrator orchestrator) : ControllerBase
{
    /// <summary>
    /// Get the current container runtime provider information.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var available = await orchestrator.IsAvailableAsync();
        return Ok(new { orchestrator.ProviderName, IsAvailable = available });
    }
}
