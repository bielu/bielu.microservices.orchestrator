using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Gateway.Contracts;
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
    /// Name of the <see cref="HttpClient"/> used to talk to the gateway.
    /// </summary>
    public const string HttpClientName = "GatewayRegistration";

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
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new GatewayRegistrationOptions();
        configure(options);
        Validate(options);

        builder.Services.AddSingleton(options);

        builder.Services.AddTransient(_ => new ApiKeyDelegatingHandler(options.ApiKey));

        builder.Services.AddHttpClient(HttpClientName, client =>
        {
            client.BaseAddress = new Uri(options.GatewayUrl.TrimEnd('/') + "/");
        })
        .AddHttpMessageHandler<ApiKeyDelegatingHandler>();

        builder.Services.AddHostedService<OrchestratorGatewayRegistrationService>();

        return builder;
    }

    private static void Validate(GatewayRegistrationOptions options)
    {
        if (!IsHttpUrl(options.GatewayUrl))
            throw new ArgumentException("GatewayUrl must be an absolute http or https URL.", "configure");

        if (!IsHttpUrl(options.InstanceAddress))
            throw new ArgumentException("InstanceAddress must be an absolute http or https URL.", "configure");

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("ApiKey must be configured for gateway authentication.", "configure");

        if (!GatewayConstants.IsValidInstanceId(options.InstanceId))
            throw new ArgumentException(
                $"InstanceId must be 1-{GatewayConstants.MaxInstanceIdLength} characters of letters, digits, '-', '_' or '.'.",
                "configure");

        if (options.HeartbeatInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException("configure", "HeartbeatInterval must be greater than zero.");
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
