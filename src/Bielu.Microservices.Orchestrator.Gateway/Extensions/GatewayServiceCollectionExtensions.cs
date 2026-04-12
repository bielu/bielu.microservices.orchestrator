using Bielu.Microservices.Orchestrator.Gateway.Authentication;
using Bielu.Microservices.Orchestrator.Gateway.Configuration;
using Bielu.Microservices.Orchestrator.Gateway.Services;
using Bielu.Microservices.Orchestrator.Gateway.Yarp;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

namespace Bielu.Microservices.Orchestrator.Gateway.Extensions;

/// <summary>
/// Extension methods for registering the YARP orchestrator gateway.
/// </summary>
public static class GatewayServiceCollectionExtensions
{
    /// <summary>
    /// Adds the YARP orchestrator gateway to the service collection.
    /// Registers authentication, registration store, cleanup service, and dynamic YARP config.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">A delegate to configure <see cref="GatewayOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOrchestratorGateway(
        this IServiceCollection services,
        Action<GatewayOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new GatewayOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("ApiKey must be configured for gateway authentication.", nameof(configure));

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

        // YARP reverse proxy with dynamic config
        services.AddSingleton<IProxyConfigProvider>(sp =>
            new DynamicOrchestratorProxyConfigProvider(
                sp.GetRequiredService<OrchestratorRegistrationStore>(),
                options.RoutePattern));

        services.AddReverseProxy();

        // Controllers for registration API
        services.AddControllers();

        return services;
    }
}
