using Bielu.Microservices.Orchestrator.Gateway.Configuration;
using Bielu.Microservices.Orchestrator.Gateway.Services;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace Bielu.Microservices.Orchestrator.Gateway.Yarp;

/// <summary>
/// Dynamic YARP proxy configuration provider that builds clusters and routes
/// from the live set of registered orchestrator instances.
/// </summary>
/// <remarks>
/// Only the destination set lives in the YARP config. Per-instance load stats change on
/// every heartbeat and are read straight from the store by
/// <see cref="LeastLoadedOrchestratorPolicy"/>, so heartbeats never trigger a config reload.
/// </remarks>
public sealed class DynamicOrchestratorProxyConfigProvider : IProxyConfigProvider, IDisposable
{
    /// <summary>Cluster id of the orchestrator destinations.</summary>
    public const string ClusterId = "orchestrators";

    /// <summary>Route id of the catch-all orchestrator route.</summary>
    public const string RouteId = "orchestrator-route";

    private readonly OrchestratorRegistrationStore _store;
    private readonly string _routePattern;
    private readonly Lock _reloadLock = new();
    private volatile DynamicProxyConfig _config;

    /// <summary>
    /// Creates a new instance of <see cref="DynamicOrchestratorProxyConfigProvider"/>
    /// and subscribes to registration changes.
    /// </summary>
    // A regular constructor (rather than a primary one) because construction subscribes to the store.
    public DynamicOrchestratorProxyConfigProvider(
        OrchestratorRegistrationStore store,
        GatewayOptions options)
    {
        _store = store;
        _routePattern = options.RoutePattern;
        _config = BuildConfig();

        _store.OnChange += Reload;
    }

    /// <inheritdoc/>
    public IProxyConfig GetConfig() => _config;

    /// <inheritdoc/>
    public void Dispose()
    {
        _store.OnChange -= Reload;
        _config.Dispose();
    }

    private void Reload()
    {
        lock (_reloadLock)
        {
            var oldConfig = _config;
            _config = BuildConfig();
            oldConfig.SignalChangeAndDispose();
        }
    }

    private DynamicProxyConfig BuildConfig()
    {
        var destinations = _store.GetAlive().ToDictionary(
            instance => instance.InstanceId,
            instance => new DestinationConfig
            {
                Address = instance.Address,
                Metadata = new Dictionary<string, string>
                {
                    ["Provider"] = instance.Provider ?? "Unknown"
                }
            });

        var cluster = new ClusterConfig
        {
            ClusterId = ClusterId,
            LoadBalancingPolicy = LeastLoadedOrchestratorPolicy.PolicyName,
            Destinations = destinations
        };

        var route = new RouteConfig
        {
            RouteId = RouteId,
            ClusterId = ClusterId,
            Match = new RouteMatch
            {
                Path = _routePattern
            }
        };

        return new DynamicProxyConfig([route], [cluster]);
    }

    /// <summary>
    /// In-memory YARP config snapshot with a cancellable change token.
    /// </summary>
    private sealed class DynamicProxyConfig : IProxyConfig, IDisposable
    {
        private readonly CancellationTokenSource _cts = new();

        public DynamicProxyConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
        {
            Routes = routes;
            Clusters = clusters;
            // Created once so it stays readable after the source is disposed.
            ChangeToken = new CancellationChangeToken(_cts.Token);
        }

        public IReadOnlyList<RouteConfig> Routes { get; }
        public IReadOnlyList<ClusterConfig> Clusters { get; }
        public IChangeToken ChangeToken { get; }

        public void SignalChangeAndDispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }

        public void Dispose() => _cts.Dispose();
    }
}
