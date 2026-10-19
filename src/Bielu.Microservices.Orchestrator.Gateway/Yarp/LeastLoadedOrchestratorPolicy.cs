using Bielu.Microservices.Orchestrator.Gateway.Configuration;
using Bielu.Microservices.Orchestrator.Gateway.Services;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Model;

namespace Bielu.Microservices.Orchestrator.Gateway.Yarp;

/// <summary>
/// YARP load-balancing policy that routes to the least-loaded orchestrator instance, scored
/// from the CPU and memory stats reported in the latest heartbeat.
/// </summary>
/// <remarks>
/// Uses "power of two choices": two random destinations are compared and the less loaded one
/// wins. Stats only refresh once per heartbeat, so always picking the global minimum would send
/// every request to the same instance until the next heartbeat.
/// </remarks>
public sealed class LeastLoadedOrchestratorPolicy(
    OrchestratorRegistrationStore store,
    GatewayOptions options) : ILoadBalancingPolicy
{
    /// <summary>The policy name referenced from the cluster config.</summary>
    public const string PolicyName = "LeastLoadedOrchestrator";

    /// <inheritdoc/>
    public string Name => PolicyName;

    /// <inheritdoc/>
    public DestinationState? PickDestination(
        HttpContext context,
        ClusterState cluster,
        IReadOnlyList<DestinationState> availableDestinations)
    {
        switch (availableDestinations.Count)
        {
            case 0:
                return null;
            case 1:
                return availableDestinations[0];
        }

        var first = Random.Shared.Next(availableDestinations.Count);
        var second = Random.Shared.Next(availableDestinations.Count - 1);
        if (second >= first)
            second++;

        var a = availableDestinations[first];
        var b = availableDestinations[second];
        return GetScore(a.DestinationId) <= GetScore(b.DestinationId) ? a : b;
    }

    /// <summary>
    /// Load score of an instance: CPU fraction plus memory fraction (relative to
    /// <see cref="GatewayOptions.MaxMemoryMbForScoring"/>), each clamped to [0, 1].
    /// Unknown instances score worst.
    /// </summary>
    public double GetScore(string instanceId)
    {
        if (!store.TryGet(instanceId, out var instance))
            return double.MaxValue;

        var cpu = Math.Clamp(instance.CpuPercent / 100.0, 0, 1);
        var memory = Math.Clamp(instance.MemoryMb / options.MaxMemoryMbForScoring, 0, 1);
        return cpu + memory;
    }
}
