using Bielu.Microservices.Orchestrator.Gateway.Authentication;
using Bielu.Microservices.Orchestrator.Gateway.Configuration;
using Bielu.Microservices.Orchestrator.Gateway.Contracts.Models;
using Bielu.Microservices.Orchestrator.Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bielu.Microservices.Orchestrator.Gateway.Controllers;

/// <summary>
/// Registration API used by orchestrator instances to announce themselves,
/// send heartbeats, and gracefully deregister.
/// </summary>
[ApiController]
[Route("api/gateway")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.AuthenticationScheme)]
public class GatewayRegistrationController(
    OrchestratorRegistrationStore store,
    GatewayOptions options) : ControllerBase
{
    /// <summary>
    /// Register a new orchestrator instance.
    /// </summary>
    [HttpPost("register")]
    public IActionResult Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InstanceId))
            return BadRequest(new { Error = "InstanceId is required." });

        if (string.IsNullOrWhiteSpace(request.Address))
            return BadRequest(new { Error = "Address is required." });

        if (!Uri.TryCreate(request.Address, UriKind.Absolute, out _))
            return BadRequest(new { Error = "Address must be a valid absolute URL." });

        var instance = new RegisteredInstance
        {
            InstanceId = request.InstanceId,
            Address = request.Address,
            Provider = request.Provider,
            CpuPercent = request.CpuPercent,
            MemoryMb = request.MemoryMb,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(options.InstanceTtlSeconds)
        };

        store.Register(instance);

        return Ok(new RegisterResponse { TtlSeconds = options.InstanceTtlSeconds });
    }

    /// <summary>
    /// Heartbeat from a registered orchestrator instance. Refreshes TTL and resource stats.
    /// </summary>
    [HttpPut("heartbeat/{instanceId}")]
    public IActionResult Heartbeat(string instanceId, [FromBody] HeartbeatRequest request)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return BadRequest(new { Error = "InstanceId is required." });

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(options.InstanceTtlSeconds);
        var found = store.Heartbeat(instanceId, request.CpuPercent, request.MemoryMb, expiresAt);

        return found ? Ok() : NotFound(new { Error = $"Instance '{instanceId}' is not registered." });
    }

    /// <summary>
    /// Graceful deregistration of an orchestrator instance.
    /// </summary>
    [HttpDelete("register/{instanceId}")]
    public IActionResult Deregister(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return BadRequest(new { Error = "InstanceId is required." });

        var found = store.Deregister(instanceId);
        return found ? NoContent() : NotFound(new { Error = $"Instance '{instanceId}' is not registered." });
    }

    /// <summary>
    /// Returns a list of all currently registered (alive) orchestrator instances.
    /// </summary>
    [HttpGet("instances")]
    public IActionResult ListInstances() => Ok(store.GetAlive());
}
