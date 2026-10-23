using System.Security.Claims;
using System.Text.Encodings.Web;
using Bielu.Microservices.Orchestrator.Gateway.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bielu.Microservices.Orchestrator.Gateway.Authentication;

/// <summary>
/// Authentication scheme defaults for API key authentication.
/// </summary>
public static class ApiKeyAuthenticationDefaults
{
    /// <summary>
    /// The authentication scheme name.
    /// </summary>
    public const string AuthenticationScheme = "ApiKey";
}

/// <summary>
/// Options for API key authentication.
/// </summary>
public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The expected API key value. Requests whose <c>X-Orchestrator-ApiKey</c> header
    /// does not match this value will be rejected.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Authentication handler that validates the API key header on incoming requests
/// from orchestrator instances.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder) : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, loggerFactory, encoder)
{
    /// <inheritdoc/>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(GatewayConstants.ApiKeyHeaderName, out var providedKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("API key header is missing."));
        }

        if (!string.Equals(providedKey, Options.ApiKey, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, "OrchestratorInstance") };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
