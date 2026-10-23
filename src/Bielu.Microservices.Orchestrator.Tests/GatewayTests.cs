using Bielu.Microservices.Orchestrator.Gateway.Configuration;
using Bielu.Microservices.Orchestrator.Gateway.Contracts.Models;
using Bielu.Microservices.Orchestrator.Gateway.Controllers;
using Bielu.Microservices.Orchestrator.Gateway.Extensions;
using Bielu.Microservices.Orchestrator.Gateway.Services;
using Bielu.Microservices.Orchestrator.Gateway.Yarp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Model;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for the YARP gateway: registration store, dynamic config, load balancing and wiring.
/// </summary>
public class GatewayTests
{
    private static OrchestratorRegistrationStore CreateStore() =>
        new(NullLogger<OrchestratorRegistrationStore>.Instance);

    private static RegisteredInstance Instance(string id, double cpu = 0, double memoryMb = 0, int ttlSeconds = 30) => new()
    {
        InstanceId = id,
        Address = $"http://{id}:5000",
        CpuPercent = cpu,
        MemoryMb = memoryMb,
        ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds)
    };

    // ---- Registration store ----

    [Fact]
    public void Register_NewThenExisting_ReportsAddedThenReplaced()
    {
        var store = CreateStore();

        store.Register(Instance("a")).ShouldBeTrue();
        store.Register(Instance("a", cpu: 10)).ShouldBeFalse();

        store.TryGet("a", out var stored).ShouldBeTrue();
        stored.CpuPercent.ShouldBe(10);
        store.GetAlive().Count.ShouldBe(1);
    }

    [Fact]
    public void Heartbeat_UpdatesStatsAndExpiry_WithoutRaisingOnChange()
    {
        var store = CreateStore();
        store.Register(Instance("a"));
        var changes = 0;
        store.OnChange += () => changes++;
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

        store.Heartbeat("a", 42, 512, expiresAt).ShouldBeTrue();

        store.TryGet("a", out var stored).ShouldBeTrue();
        stored.CpuPercent.ShouldBe(42);
        stored.MemoryMb.ShouldBe(512);
        stored.ExpiresAt.ShouldBe(expiresAt);
        changes.ShouldBe(0);
    }

    [Fact]
    public void Heartbeat_UnknownInstance_ReturnsFalse()
    {
        CreateStore().Heartbeat("missing", 0, 0, DateTimeOffset.UtcNow).ShouldBeFalse();
    }

    [Fact]
    public void RemoveExpired_RemovesOnlyExpiredInstances_AndRaisesOnChange()
    {
        var store = CreateStore();
        store.Register(Instance("alive"));
        store.Register(Instance("expired", ttlSeconds: -1));
        var changes = 0;
        store.OnChange += () => changes++;

        store.RemoveExpired().ShouldBe(1);

        store.TryGet("expired", out _).ShouldBeFalse();
        store.TryGet("alive", out _).ShouldBeTrue();
        changes.ShouldBe(1);
    }

    [Fact]
    public void Deregister_RemovesInstance()
    {
        var store = CreateStore();
        store.Register(Instance("a"));

        store.Deregister("a").ShouldBeTrue();
        store.Deregister("a").ShouldBeFalse();
        store.GetAlive().ShouldBeEmpty();
    }

    // ---- Dynamic YARP config ----

    [Fact]
    public void ConfigProvider_ReloadsOnRegistration_AndUsesLeastLoadedPolicy()
    {
        var store = CreateStore();
        using var provider = new DynamicOrchestratorProxyConfigProvider(store, new GatewayOptions { ApiKey = "k" });
        var initial = provider.GetConfig();
        initial.Clusters.Single().Destinations.ShouldBeEmpty();

        store.Register(Instance("a") with { Provider = "Docker" });

        initial.ChangeToken.HasChanged.ShouldBeTrue();
        var cluster = provider.GetConfig().Clusters.Single();
        cluster.LoadBalancingPolicy.ShouldBe(LeastLoadedOrchestratorPolicy.PolicyName);
        var destination = cluster.Destinations!["a"];
        destination.Address.ShouldBe("http://a:5000");
        destination.Metadata!["Provider"].ShouldBe("Docker");
    }

    [Fact]
    public void ConfigProvider_DoesNotReloadOnHeartbeat()
    {
        var store = CreateStore();
        store.Register(Instance("a"));
        using var provider = new DynamicOrchestratorProxyConfigProvider(store, new GatewayOptions { ApiKey = "k" });
        var config = provider.GetConfig();

        store.Heartbeat("a", 90, 100, DateTimeOffset.UtcNow.AddSeconds(30));

        config.ChangeToken.HasChanged.ShouldBeFalse();
    }

    // ---- Load balancing ----

    [Fact]
    public void LeastLoadedPolicy_PicksLessLoadedDestination_UsingLatestHeartbeat()
    {
        var store = CreateStore();
        store.Register(Instance("busy"));
        store.Register(Instance("idle"));
        store.Heartbeat("busy", 95, 3000, DateTimeOffset.UtcNow.AddSeconds(30));
        store.Heartbeat("idle", 5, 200, DateTimeOffset.UtcNow.AddSeconds(30));
        var policy = new LeastLoadedOrchestratorPolicy(store, new GatewayOptions { ApiKey = "k" });
        DestinationState[] destinations = [new("busy"), new("idle")];

        for (var i = 0; i < 20; i++)
        {
            policy.PickDestination(new DefaultHttpContext(), new ClusterState("orchestrators"), destinations)!
                .DestinationId.ShouldBe("idle");
        }
    }

    [Fact]
    public void LeastLoadedPolicy_NoDestinations_ReturnsNull()
    {
        var policy = new LeastLoadedOrchestratorPolicy(CreateStore(), new GatewayOptions { ApiKey = "k" });

        policy.PickDestination(new DefaultHttpContext(), new ClusterState("orchestrators"), []).ShouldBeNull();
    }

    [Fact]
    public void LeastLoadedPolicy_Score_CombinesClampedCpuAndMemoryFractions()
    {
        var store = CreateStore();
        store.Register(Instance("a", cpu: 50, memoryMb: 1024));
        store.Register(Instance("b", cpu: 250, memoryMb: 99999));
        var policy = new LeastLoadedOrchestratorPolicy(store, new GatewayOptions { ApiKey = "k", MaxMemoryMbForScoring = 2048 });

        policy.GetScore("a").ShouldBe(1.0);
        policy.GetScore("b").ShouldBe(2.0);
        policy.GetScore("unknown").ShouldBe(double.MaxValue);
    }

    // ---- Controller validation ----

    [Theory]
    [InlineData("ftp://host:21")]
    [InlineData("file:///etc/passwd")]
    [InlineData("not-a-url")]
    public void Register_NonHttpAddress_ReturnsBadRequest(string address)
    {
        var store = CreateStore();
        var controller = new GatewayRegistrationController(store, new GatewayOptions { ApiKey = "k" });

        var result = controller.Register(new RegisterRequest { InstanceId = "a", Address = address });

        result.ShouldBeOfType<BadRequestObjectResult>();
        store.GetAlive().ShouldBeEmpty();
    }

    [Fact]
    public void Register_InvalidInstanceId_ReturnsBadRequest()
    {
        var controller = new GatewayRegistrationController(CreateStore(), new GatewayOptions { ApiKey = "k" });

        controller.Register(new RegisterRequest { InstanceId = "a/b", Address = "http://a:5000" })
            .ShouldBeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void Register_ValidRequest_ReturnsTtl()
    {
        var controller = new GatewayRegistrationController(CreateStore(), new GatewayOptions { ApiKey = "k", InstanceTtlSeconds = 45 });

        var result = controller.Register(new RegisterRequest { InstanceId = "a", Address = "https://a:5001" });

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<RegisterResponse>().TtlSeconds.ShouldBe(45);
    }

    // ---- DI wiring ----

    [Fact]
    public void AddOrchestratorGateway_RegistersStoreConfigProviderAndPolicy()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddOrchestratorGateway(o => o.ApiKey = "secret");

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<OrchestratorRegistrationStore>().ShouldNotBeNull();
        provider.GetRequiredService<IProxyConfigProvider>().ShouldBeOfType<DynamicOrchestratorProxyConfigProvider>();
        provider.GetServices<ILoadBalancingPolicy>()
            .ShouldContain(p => p.Name == LeastLoadedOrchestratorPolicy.PolicyName);
    }

    [Fact]
    public void AddOrchestratorGateway_PlaceholderApiKey_Throws()
    {
        Should.Throw<ArgumentException>(() => new ServiceCollection()
            .AddOrchestratorGateway(o => o.ApiKey = GatewayServiceCollectionExtensions.PlaceholderApiKey));
    }

    [Theory]
    [InlineData(0, 4096)]
    [InlineData(30, 0)]
    public void AddOrchestratorGateway_NonPositiveBounds_Throws(int ttlSeconds, double maxMemoryMb)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddOrchestratorGateway(o =>
        {
            o.ApiKey = "secret";
            o.InstanceTtlSeconds = ttlSeconds;
            o.MaxMemoryMbForScoring = maxMemoryMb;
        }));
    }

    [Fact]
    public void AddOrchestratorGateway_EmptyRoutePattern_Throws()
    {
        Should.Throw<ArgumentException>(() => new ServiceCollection().AddOrchestratorGateway(o =>
        {
            o.ApiKey = "secret";
            o.RoutePattern = "";
        }));
    }
}
