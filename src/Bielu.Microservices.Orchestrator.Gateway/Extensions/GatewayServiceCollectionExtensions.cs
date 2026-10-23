using Bielu.Microservices.Orchestrator.Gateway.Authentication;
using Bielu.Microservices.Orchestrator.Gateway.Configuration;
using Bielu.Microservices.Orchestrator.Gateway.Services;
using Bielu.Microservices.Orchestrator.Gateway.Yarp;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.LoadBalancing;

namespace Bielu.Microservices.Orchestrator.Gateway.Extensions;

/// <summary>
/// Extension methods for registering the YARP orchestrator gateway.
/// </summary>
public static class GatewayServiceCollectionExtensions
{
    /// <summary>
    /// Placeholder API key shipped in <c>appsettings.json</c>; the gateway refuses to start with it.
    /// </summary>
    public const string PlaceholderApiKey = "CHANGE-ME-TO-A-STRONG-KEY";

    /// <summary>
    /// Adds the YARP orchestrator gateway to the service collection.
    /// Registers authentication, registration store, cleanup service, dynamic YARP config
    /// and the least-loaded load-balancing policy.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">A delegate to configure <see cref="GatewayOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOrchestratorGateway(
        this IServiceCollection services,
        Action<GatewayOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new GatewayOptions();
        configure(options);
        Validate(options);

        services.AddSingleton(options);

        // Authentication
        services.AddAuthentication(ApiKeyAuthenticationDefaults.AuthenticationScheme)
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationDefaults.AuthenticationScheme,
                authOptions =>
                {
                    authOptions.ApiKey = options.ApiKey;
                });

        services.AddAuthorization();

        // Registration store + cleanup
        services.AddSingleton<OrchestratorRegistrationStore>();
        services.AddHostedService<RegistrationCleanupService>();

        // YARP reverse proxy with dynamic config and load-aware balancing
        services.AddSingleton<IProxyConfigProvider, DynamicOrchestratorProxyConfigProvider>();
        services.AddSingleton<ILoadBalancingPolicy, LeastLoadedOrchestratorPolicy>();
        services.AddReverseProxy();

        // Controllers for registration API
        services.AddControllers();

        return services;
    }

    private static void Validate(GatewayOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("ApiKey must be configured for gateway authentication.", "configure");

        if (string.Equals(options.ApiKey, PlaceholderApiKey, StringComparison.Ordinal))
            throw new ArgumentException("Default placeholder API key must be changed before running the gateway.", "configure");

        if (options.InstanceTtlSeconds <= 0)
            throw new ArgumentOutOfRangeException("configure", "InstanceTtlSeconds must be greater than 0.");

        if (options.MaxMemoryMbForScoring <= 0)
            throw new ArgumentOutOfRangeException("configure", "MaxMemoryMbForScoring must be greater than 0.");

        if (string.IsNullOrWhiteSpace(options.RoutePattern))
            throw new ArgumentException("RoutePattern must be configured for gateway routing.", "configure");
    }
}
