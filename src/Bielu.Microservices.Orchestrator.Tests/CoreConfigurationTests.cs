using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Podman;
using Bielu.Microservices.Orchestrator.Podman.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for <see cref="OrchestratorBuilder"/>, <see cref="OrchestratorOptions"/>, the default
/// <see cref="INetworkManager"/> members and the Podman provider wiring.
/// </summary>
public class CoreConfigurationTests
{
    [Fact]
    public void AddMicroservicesOrchestrator_DeferredDecorators_RunInPriorityOrder()
    {
        // Arrange
        var services = new ServiceCollection();
        var calls = new List<string>();

        // Act
        services.AddMicroservicesOrchestrator(builder =>
        {
            builder.AddDeferredDecorator(900, _ => calls.Add("outer"));
            builder.AddDeferredDecorator(100, _ => calls.Add("inner-1"));
            builder.AddDeferredDecorator(100, _ => calls.Add("inner-2"));
        });

        // Assert
        calls.ShouldBe(["inner-1", "inner-2", "outer"]);
    }

    [Fact]
    public void AddMicroservicesOrchestrator_DeferredDecorator_ReceivesTheServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();
        IServiceCollection? received = null;

        // Act
        services.AddMicroservicesOrchestrator(builder => builder.AddDeferredDecorator(1, s => received = s));

        // Assert
        received.ShouldBeSameAs(services);
    }

    [Fact]
    public void ResolveDefaultNetworkName_NotSet_DerivesASessionNameFromTheOrchestratorId()
    {
        // Arrange
        var options = new OrchestratorOptions { OrchestratorId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e") };

        // Act & Assert
        options.ResolveDefaultNetworkName().ShouldBe("bielu-session-network-0f8fad5bd9cb469fa16570867728950e");
    }

    [Fact]
    public void ResolveDefaultNetworkName_Empty_DisablesTheDefaultNetwork()
    {
        // Arrange
        var options = new OrchestratorOptions { DefaultNetworkName = string.Empty };

        // Act & Assert
        options.ResolveDefaultNetworkName().ShouldBeNull();
    }

    [Fact]
    public void ResolveDefaultNetworkName_Explicit_IsReturnedAsIs()
    {
        // Arrange
        var options = new OrchestratorOptions { DefaultNetworkName = "shared-net" };

        // Act & Assert
        options.ResolveDefaultNetworkName().ShouldBe("shared-net");
    }

    [Fact]
    public async Task ConnectAsync_WithAttachment_DefaultsToTheAliasOverload()
    {
        // Arrange
        var manager = new AliasOnlyNetworkManager();
        var attachment = new NetworkAttachment { Aliases = ["web", "www"] };

        // Act
        await ((INetworkManager)manager).ConnectAsync("net-1", "ctr-1", attachment);

        // Assert
        manager.LastAliases.ShouldBe(["web", "www"]);
    }

    [Fact]
    public async Task PodmanOrchestrator_RuntimeReachable_IsAvailable()
    {
        // Arrange
        var containers = Substitute.For<IContainerManager>();
        var orchestrator = CreatePodman(containers);

        // Act
        var available = await orchestrator.IsAvailableAsync();

        // Assert
        available.ShouldBeTrue();
        orchestrator.ProviderName.ShouldBe("Podman");
        orchestrator.Containers.ShouldBeSameAs(containers);
    }

    [Fact]
    public async Task PodmanOrchestrator_RuntimeUnreachable_IsNotAvailable()
    {
        // Arrange
        var containers = Substitute.For<IContainerManager>();
        containers.ListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<ContainerInfo>>(new HttpRequestException("socket missing")));
        var orchestrator = CreatePodman(containers);

        // Act
        var available = await orchestrator.IsAvailableAsync();

        // Assert
        available.ShouldBeFalse();
    }

    [Fact]
    public void PodmanOptions_Defaults_PointAtThePlatformSocket()
    {
        // Act
        var options = new PodmanOptions();

        // Assert
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
        if (OperatingSystem.IsWindows())
        {
            options.Endpoint.ShouldBe("npipe://./pipe/podman-machine-default");
        }
        else
        {
            options.Endpoint.ShouldStartWith("unix:///run/user/");
            options.Endpoint.ShouldEndWith("/podman/podman.sock");
        }
    }

    private static PodmanContainerOrchestrator CreatePodman(IContainerManager containers) => new(
        containers,
        Substitute.For<IImageManager>(),
        Substitute.For<INetworkManager>(),
        Substitute.For<IVolumeManager>(),
        Substitute.For<ILogger<PodmanContainerOrchestrator>>());

    /// <summary>
    /// Network manager that only implements the alias overload, so the interface's default
    /// <see cref="INetworkManager.ConnectAsync(string, string, NetworkAttachment, CancellationToken)"/> runs.
    /// </summary>
    private sealed class AliasOnlyNetworkManager : INetworkManager
    {
        public IEnumerable<string>? LastAliases { get; private set; }

        public Task<IReadOnlyList<NetworkInfo>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> CreateAsync(string name, string driver = "bridge", CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RemoveAsync(string networkId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ConnectAsync(string networkId, string containerId, IEnumerable<string>? aliases = null, CancellationToken cancellationToken = default)
        {
            LastAliases = aliases;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(string networkId, string containerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
