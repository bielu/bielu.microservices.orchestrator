using Bielu.Microservices.Orchestrator.Gateway.Services;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace Bielu.Microservices.Orchestrator.Gateway.Yarp;

/// <summary>
/// Dynamic YARP proxy configuration provider that builds clusters and routes
/// from the live set of registered orchestrator instances.
/// </summary>
public sealed class DynamicOrchestratorProxyConfigProvider : IProxyConfigProvider, IDisposable
{
    private readonly OrchestratorRegistrationStore _store;
    private readonly string _routePattern;
    private volatile DynamicProxyConfig _config;

    /// <summary>
    /// Creates a new instance of <see cref="DynamicOrchestratorProxyConfigProvider"/>.
    /// </summary>
    public DynamicOrchestratorProxyConfigProvider(
        OrchestratorRegistrationStore store,
        string routePattern)
    {
        _store = store;
        _routePattern = routePattern;
        _config = BuildConfig();

        _store.OnChange += Reload;
    }

    /// <inheritdoc/>
    public IProxyConfig GetConfig() => _config;

    /// <inheritdoc/>
    public void Dispose() => _store.OnChange -= Reload;

    private void Reload()
    {
        var oldConfig = _config;
        _config = BuildConfig();
        oldConfig.SignalChange();
    }

    private DynamicProxyConfig BuildConfig()
    {
        var alive = _store.GetAlive();

        var destinations = new Dictionary<string, DestinationConfig>();
        foreach (var instance in alive)
        {
            destinations[instance.InstanceId] = new DestinationConfig
            {
                Address = instance.Address,
                Metadata = new Dictionary<string, string>
                {
                    ["CpuPercent"] = instance.CpuPercent.ToString("F2"),
                    ["MemoryMb"] = instance.MemoryMb.ToString("F2"),
                    ["Provider"] = instance.Provider ?? "Unknown"
                }
            };
        }

        var cluster = new ClusterConfig
        {
            ClusterId = "orchestrators",
            Destinations = destinations
        };

        var route = new RouteConfig
        {
            RouteId = "orchestrator-route",
            ClusterId = "orchestrators",
            Match = new RouteMatch
            {
                Path = _routePattern
            }
        };

        return new DynamicProxyConfig(
            [route],
            [cluster]);
    }

    /// <summary>
    /// In-memory YARP config snapshot with a cancellable change token.
    /// </summary>
    private sealed class DynamicProxyConfig(
        IReadOnlyList<RouteConfig> routes,
        IReadOnlyList<ClusterConfig> clusters) : IProxyConfig
    {
        private readonly CancellationTokenSource _cts = new();

        public IReadOnlyList<RouteConfig> Routes { get; } = routes;
        public IReadOnlyList<ClusterConfig> Clusters { get; } = clusters;
        public IChangeToken ChangeToken => new CancellationChangeToken(_cts.Token);

        public void SignalChange() => _cts.Cancel();
    }
}
