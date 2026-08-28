using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Gateway.Registration.Authentication;
using Bielu.Microservices.Orchestrator.Gateway.Registration.Configuration;
using Bielu.Microservices.Orchestrator.Gateway.Registration.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bielu.Microservices.Orchestrator.Gateway.Registration.Extensions;

/// <summary>
/// Extension methods for registering the gateway registration client on the orchestrator side.
/// </summary>
public static class GatewayRegistrationBuilderExtensions
{
    /// <summary>
    /// Adds gateway auto-registration to the orchestrator. The orchestrator will register
    /// itself with the YARP gateway on startup and send periodic heartbeats.
    /// </summary>
    /// <param name="builder">The orchestrator builder.</param>
    /// <param name="configure">A delegate to configure <see cref="GatewayRegistrationOptions"/>.</param>
    /// <returns>The orchestrator builder for chaining.</returns>
    public static OrchestratorBuilder AddGatewayRegistration(
        this OrchestratorBuilder builder,
        Action<GatewayRegistrationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new GatewayRegistrationOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.GatewayUrl))
            throw new ArgumentException("GatewayUrl must be configured.", nameof(configure));

        if (string.IsNullOrWhiteSpace(options.InstanceAddress))
            throw new ArgumentException("InstanceAddress must be configured.", nameof(configure));

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("ApiKey must be configured for gateway authentication.", nameof(configure));

        builder.Services.AddSingleton(options);

        builder.Services.AddTransient(_ => new ApiKeyDelegatingHandler(options.ApiKey));

        builder.Services.AddHttpClient("GatewayRegistration", client =>
        {
            client.BaseAddress = new Uri(options.GatewayUrl.TrimEnd('/') + "/");
        })
        .AddHttpMessageHandler<ApiKeyDelegatingHandler>();

        builder.Services.AddHostedService<OrchestratorGatewayRegistrationService>();

        return builder;
    }
}
