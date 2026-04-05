using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Microsoft.AspNetCore.Mvc;

namespace Bielu.Microservices.Orchestrator.Examples.Api.Controllers;

/// <summary>
/// Image management endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ImagesController(IContainerOrchestrator orchestrator) : ControllerBase
{
    /// <summary>
    /// List all images.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await orchestrator.Images.ListAsync());

    /// <summary>
    /// Pull an image from a registry.
    /// </summary>
    /// <remarks>
    /// Example body: { "image": "nginx", "tag": "latest" }
    /// </remarks>
    [HttpPost("pull")]
    public async Task<IActionResult> Pull([FromBody] PullImageRequest request)
    {
        await orchestrator.Images.PullAsync(request);
        return Ok(new { Message = $"Pulled {request.Image}:{request.Tag}" });
    }

    /// <summary>
    /// Remove an image.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Remove(string id, bool force = false)
    {
        await orchestrator.Images.RemoveAsync(id, force);
        return NoContent();
    }
}
