using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Microsoft.AspNetCore.Mvc;

namespace Bielu.Microservices.Orchestrator.Examples.Api.Controllers;

/// <summary>
/// Endpoints for detecting and applying image updates for managed instances.
/// </summary>
[ApiController]
[Route("api/image-updates")]
public class ImageUpdatesController(IImageUpdateService updates) : ControllerBase
{
    /// <summary>
    /// Returns the image update status for every managed instance.
    /// </summary>
    /// <remarks>
    /// Query parameter <c>pull=false</c> skips pulling from the registry and compares
    /// only against the locally cached image.
    /// </remarks>
    [HttpGet]
    public async Task<IActionResult> CheckAll(bool pull = true, CancellationToken cancellationToken = default)
    {
        var statuses = await updates.CheckAllAsync(
            new ImageUpdateOptions { Pull = pull },
            cancellationToken);
        return Ok(statuses);
    }

    /// <summary>
    /// Returns the image update status for a single managed instance.
    /// </summary>
    [HttpGet("{instanceId}")]
    public async Task<IActionResult> Check(string instanceId, bool pull = true,
        CancellationToken cancellationToken = default)
    {
        var status = await updates.CheckAsync(
            instanceId,
            new ImageUpdateOptions { Pull = pull },
            cancellationToken);
        return Ok(status);
    }

    /// <summary>
    /// Pulls the latest image for the instance and recreates its containers when the
    /// image digest changed. Pass <c>{"force": true}</c> to recreate unconditionally.
    /// </summary>
    [HttpPost("{instanceId}")]
    public async Task<IActionResult> Update(
        string instanceId,
        [FromBody] ImageUpdateOptions? options,
        CancellationToken cancellationToken = default)
    {
        var result = await updates.UpdateAsync(instanceId, options, cancellationToken);
        return Ok(result);
    }
}
