using System.Net.Http.Headers;
using Bielu.Microservices.Orchestrator.Gateway.Contracts;

namespace Bielu.Microservices.Orchestrator.Gateway.Registration.Authentication;

/// <summary>
/// HTTP message handler that attaches the API key header to every outgoing request
/// to the gateway.
/// </summary>
public sealed class ApiKeyDelegatingHandler(string apiKey) : DelegatingHandler
{
    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove(GatewayConstants.ApiKeyHeaderName);
        request.Headers.Add(GatewayConstants.ApiKeyHeaderName, apiKey);
        return base.SendAsync(request, cancellationToken);
    }
}
