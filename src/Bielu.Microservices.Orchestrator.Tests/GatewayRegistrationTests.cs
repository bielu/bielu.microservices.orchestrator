using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Gateway.Contracts;
using Bielu.Microservices.Orchestrator.Gateway.Registration.Configuration;
using Bielu.Microservices.Orchestrator.Gateway.Registration.Extensions;
using Bielu.Microservices.Orchestrator.Gateway.Registration.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for the orchestrator-side gateway registration client wiring.
/// </summary>
public class GatewayRegistrationTests
{
    private static void ValidOptions(GatewayRegistrationOptions options)
    {
        options.GatewayUrl = "http://gateway:8080";
        options.InstanceAddress = "http://orchestrator-1:5000";
        options.ApiKey = "secret";
        options.InstanceId = "orchestrator-1";
    }

    private static ServiceCollection AddRegistration(Action<GatewayRegistrationOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMicroservicesOrchestrator(builder => builder.AddGatewayRegistration(configure));
        return services;
    }

    [Fact]
    public void AddGatewayRegistration_RegistersOptionsHttpClientAndHostedService()
    {
        // Act
        var services = AddRegistration(ValidOptions);

        // Assert
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<GatewayRegistrationOptions>().InstanceId.ShouldBe("orchestrator-1");
        provider.GetServices<IHostedService>().ShouldContain(s => s is OrchestratorGatewayRegistrationService);

        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(GatewayRegistrationBuilderExtensions.HttpClientName);
        client.BaseAddress.ShouldBe(new Uri("http://gateway:8080/"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://gateway")]
    public void AddGatewayRegistration_InvalidGatewayUrl_Throws(string gatewayUrl)
    {
        Should.Throw<ArgumentException>(() => AddRegistration(o =>
        {
            ValidOptions(o);
            o.GatewayUrl = gatewayUrl;
        }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("file:///etc/passwd")]
    public void AddGatewayRegistration_InvalidInstanceAddress_Throws(string address)
    {
        Should.Throw<ArgumentException>(() => AddRegistration(o =>
        {
            ValidOptions(o);
            o.InstanceAddress = address;
        }));
    }

    [Fact]
    public void AddGatewayRegistration_MissingApiKey_Throws()
    {
        Should.Throw<ArgumentException>(() => AddRegistration(o =>
        {
            ValidOptions(o);
            o.ApiKey = " ";
        }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has/slash")]
    [InlineData("has space")]
    [InlineData("query?x")]
    public void AddGatewayRegistration_InvalidInstanceId_Throws(string instanceId)
    {
        Should.Throw<ArgumentException>(() => AddRegistration(o =>
        {
            ValidOptions(o);
            o.InstanceId = instanceId;
        }));
    }

    [Fact]
    public void AddGatewayRegistration_NonPositiveHeartbeatInterval_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => AddRegistration(o =>
        {
            ValidOptions(o);
            o.HeartbeatInterval = TimeSpan.Zero;
        }));
    }

    [Fact]
    public async Task RegistrationService_RetriesUntilRegistered_SendsProvider_AndDeregistersOnStop()
    {
        // Arrange — the gateway rejects the first registration attempt, then accepts
        var handler = new RecordingHandler(failFirstRegistration: true);
        var orchestrator = NSubstitute.Substitute.For<Bielu.Microservices.Orchestrator.Abstractions.IContainerOrchestrator>();
        orchestrator.ProviderName.Returns("Fake");

        var services = AddRegistration(o =>
        {
            ValidOptions(o);
            o.HeartbeatInterval = TimeSpan.FromMinutes(5);
        });
        services.AddSingleton(orchestrator);
        services.AddHttpClient(GatewayRegistrationBuilderExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        var service = provider.GetServices<IHostedService>().OfType<OrchestratorGatewayRegistrationService>().Single();

        // Act
        await service.StartAsync(CancellationToken.None);
        await handler.Registered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await service.StopAsync(CancellationToken.None);

        // Assert
        handler.Requests.Count(r => r.Method == HttpMethod.Post).ShouldBe(2);
        handler.RegisteredBody.ShouldContain("\"provider\":\"Fake\"");
        handler.Requests.ShouldContain(r =>
            r.Method == HttpMethod.Delete && r.Path == "/api/gateway/register/orchestrator-1");
        handler.Requests.ShouldAllBe(r => r.ApiKey == "secret");
    }

    private sealed class RecordingHandler(bool failFirstRegistration) : HttpMessageHandler
    {
        private int _registrations;

        public List<(HttpMethod Method, string Path, string? ApiKey)> Requests { get; } = [];
        public TaskCompletionSource Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? RegisteredBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.Headers.TryGetValues(GatewayConstants.ApiKeyHeaderName, out var keys);
            lock (Requests)
                Requests.Add((request.Method, request.RequestUri!.AbsolutePath, keys?.SingleOrDefault()));

            if (request.Method != HttpMethod.Post)
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);

            if (Interlocked.Increment(ref _registrations) == 1 && failFirstRegistration)
                return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);

            RegisteredBody = await request.Content!.ReadAsStringAsync(ct);
            Registered.TrySetResult();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { ttlSeconds = 30 })
            };
        }
    }

    [Fact]
    public void GatewayRegistrationOptions_DefaultInstanceId_IsValid()
    {
        GatewayConstants.IsValidInstanceId(new GatewayRegistrationOptions().InstanceId).ShouldBeTrue();
    }

    [Theory]
    [InlineData("orchestrator-1", true)]
    [InlineData("node_a.eu-west", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("a/b", false)]
    [InlineData("a#b", false)]
    public void IsValidInstanceId_ValidatesUrlSafeCharacters(string? instanceId, bool expected)
    {
        GatewayConstants.IsValidInstanceId(instanceId).ShouldBe(expected);
    }

    [Fact]
    public void IsValidInstanceId_TooLong_ReturnsFalse()
    {
        GatewayConstants.IsValidInstanceId(new string('a', GatewayConstants.MaxInstanceIdLength + 1)).ShouldBeFalse();
    }
}
