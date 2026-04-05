using Bielu.Microservices.Orchestrator.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Bielu.Microservices.Orchestrator.Examples.Api.Controllers;

/// <summary>
/// Network management endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class NetworksController(IContainerOrchestrator orchestrator) : ControllerBase
{
    /// <summary>
    /// List all networks.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await orchestrator.Networks.ListAsync());
}
