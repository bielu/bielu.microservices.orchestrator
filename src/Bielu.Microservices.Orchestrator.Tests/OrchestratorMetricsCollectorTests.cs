using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.OpenTelemetry.Instrumentation;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for <see cref="OrchestratorMetricsCollector"/>.
/// </summary>
public class OrchestratorMetricsCollectorTests
{
    private readonly IInstanceStore _instanceStore = Substitute.For<IInstanceStore>();
    private readonly IContainerManager _containerManager = Substitute.For<IContainerManager>();
    private readonly ILogger<OrchestratorMetricsCollector> _logger = Substitute.For<ILogger<OrchestratorMetricsCollector>>();

    private OrchestratorMetricsCollector CreateCollector() =>
        new(_instanceStore, _containerManager, _logger);

    // -----------------------------------------------------------------------
    // Managed instance counts
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CollectMetrics_NoInstances_AllCountsAreZero()
    {
        _instanceStore.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ManagedInstance>().AsReadOnly());
        _containerManager.ListAsync(true, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ContainerInfo>().AsReadOnly());

        using var collector = CreateCollector();
        await collector.CollectMetricsAsync();

        collector.TotalManagedInstances.ShouldBe(0);
        collector.HealthyInstances.ShouldBe(0);
        collector.TotalContainers.ShouldBe(0);
        collector.TotalDesiredReplicas.ShouldBe(0);
    }

    [Fact]
    public async Task CollectMetrics_WithRunningContainers_CountsHealthyInstances()
    {
        var instances = new List<ManagedInstance>
        {
            new()
            {
                Id = "inst-1",
                ContainerIds = ["ctr-1", "ctr-2"],
                DesiredState = DesiredState.Running,
                DesiredReplicas = 2,
                ProviderName = "Docker",
                OriginalRequest = new CreateContainerRequest { Image = "nginx:1.25" }
            },
            new()
            {
                Id = "inst-2",
                ContainerIds = ["ctr-3"],
                DesiredState = DesiredState.Running,
                DesiredReplicas = 1,
                ProviderName = "Docker",
                OriginalRequest = new CreateContainerRequest { Image = "redis:7" }
            }
        };

        var containers = new List<ContainerInfo>
        {
            new() { Id = "ctr-1", Name = "nginx-0", Image = "nginx:1.25", State = ContainerState.Running },
            new() { Id = "ctr-2", Name = "nginx-1", Image = "nginx:1.25", State = ContainerState.Running },
            new() { Id = "ctr-3", Name = "redis-0", Image = "redis:7", State = ContainerState.Exited }
        };

        _instanceStore.GetAllAsync(Arg.Any<CancellationToken>()).Returns(instances.AsReadOnly());
        _containerManager.ListAsync(true, Arg.Any<CancellationToken>()).Returns(containers.AsReadOnly());

        using var collector = CreateCollector();
        await collector.CollectMetricsAsync();

        collector.TotalManagedInstances.ShouldBe(2);
        collector.HealthyInstances.ShouldBe(1); // Only inst-1 has all containers Running
        collector.TotalContainers.ShouldBe(3);
        collector.TotalDesiredReplicas.ShouldBe(3);
    }

    [Fact]
    public async Task CollectMetrics_WithStoppedInstances_CategorizesByState()
    {
        var instances = new List<ManagedInstance>
        {
            new()
            {
                Id = "inst-1",
                ContainerIds = ["ctr-1"],
                DesiredState = DesiredState.Running,
                DesiredReplicas = 1,
                ProviderName = "Docker"
            },
            new()
            {
                Id = "inst-2",
                ContainerIds = ["ctr-2"],
                DesiredState = DesiredState.Stopped,
                DesiredReplicas = 1,
                ProviderName = "Kubernetes"
            }
        };

        var containers = new List<ContainerInfo>
        {
            new() { Id = "ctr-1", Image = "nginx:latest", State = ContainerState.Running },
            new() { Id = "ctr-2", Image = "redis:7", State = ContainerState.Exited }
        };

        _instanceStore.GetAllAsync(Arg.Any<CancellationToken>()).Returns(instances.AsReadOnly());
        _containerManager.ListAsync(true, Arg.Any<CancellationToken>()).Returns(containers.AsReadOnly());

        using var collector = CreateCollector();
        await collector.CollectMetricsAsync();

        collector.InstancesByState.ShouldContainKey("healthy");
        collector.InstancesByState["healthy"].ShouldBe(1);
        collector.InstancesByState.ShouldContainKey("stopped");
        collector.InstancesByState["stopped"].ShouldBe(1);
    }

    // -----------------------------------------------------------------------
    // Provider breakdown
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CollectMetrics_GroupsInstancesByProvider()
    {
        var instances = new List<ManagedInstance>
        {
            new() { Id = "a", DesiredState = DesiredState.Running, DesiredReplicas = 1, ProviderName = "Docker", ContainerIds = ["c1"] },
            new() { Id = "b", DesiredState = DesiredState.Running, DesiredReplicas = 1, ProviderName = "Docker", ContainerIds = ["c2"] },
            new() { Id = "c", DesiredState = DesiredState.Running, DesiredReplicas = 1, ProviderName = "Kubernetes", ContainerIds = ["c3"] }
        };

        _instanceStore.GetAllAsync(Arg.Any<CancellationToken>()).Returns(instances.AsReadOnly());
        _containerManager.ListAsync(true, Arg.Any<CancellationToken>())
            .Returns(new List<ContainerInfo>
            {
                new() { Id = "c1", Image = "app:1", State = ContainerState.Running },
                new() { Id = "c2", Image = "app:1", State = ContainerState.Running },
                new() { Id = "c3", Image = "app:2", State = ContainerState.Running }
            }.AsReadOnly());

        using var collector = CreateCollector();
        await collector.CollectMetricsAsync();

        collector.InstancesByProvider.ShouldContainKey("Docker");
        collector.InstancesByProvider["Docker"].ShouldBe(2);
        collector.InstancesByProvider.ShouldContainKey("Kubernetes");
        collector.InstancesByProvider["Kubernetes"].ShouldBe(1);
    }

    [Fact]
    public async Task CollectMetrics_EmptyProviderName_DefaultsToUnknown()
    {
        var instances = new List<ManagedInstance>
        {
            new() { Id = "a", DesiredState = DesiredState.Running, DesiredReplicas = 1, ProviderName = "", ContainerIds = ["c1"] }
        };

        _instanceStore.GetAllAsync(Arg.Any<CancellationToken>()).Returns(instances.AsReadOnly());
        _containerManager.ListAsync(true, Arg.Any<CancellationToken>())
            .Returns(new List<ContainerInfo>
            {
                new() { Id = "c1", Image = "app:v1", State = ContainerState.Running }
            }.AsReadOnly());

        using var collector = CreateCollector();
        await collector.CollectMetricsAsync();

        collector.InstancesByProvider.ShouldContainKey("unknown");
    }

    // -----------------------------------------------------------------------
    // Container image breakdown
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CollectMetrics_GroupsContainersByImage()
    {
        _instanceStore.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ManagedInstance>().AsReadOnly());

        _containerManager.ListAsync(true, Arg.Any<CancellationToken>())
            .Returns(new List<ContainerInfo>
            {
                new() { Id = "c1", Image = "nginx:1.25", State = ContainerState.Running },
                new() { Id = "c2", Image = "nginx:1.25", State = ContainerState.Running },
                new() { Id = "c3", Image = "redis:7", State = ContainerState.Running }
            }.AsReadOnly());

        using var collector = CreateCollector();
        await collector.CollectMetricsAsync();

        collector.ContainersByImage.ShouldContainKey("nginx:1.25");
        collector.ContainersByImage["nginx:1.25"].ShouldBe(2);
        collector.ContainersByImage.ShouldContainKey("redis:7");
        collector.ContainersByImage["redis:7"].ShouldBe(1);
    }

    // -----------------------------------------------------------------------
    // Container state breakdown
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CollectMetrics_GroupsContainersByState()
    {
        _instanceStore.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ManagedInstance>().AsReadOnly());

        _containerManager.ListAsync(true, Arg.Any<CancellationToken>())
            .Returns(new List<ContainerInfo>
            {
                new() { Id = "c1", Image = "app:1", State = ContainerState.Running },
                new() { Id = "c2", Image = "app:1", State = ContainerState.Running },
                new() { Id = "c3", Image = "app:1", State = ContainerState.Exited }
            }.AsReadOnly());

        using var collector = CreateCollector();
        await collector.CollectMetricsAsync();

        collector.ContainersByState.ShouldContainKey("Running");
        collector.ContainersByState["Running"].ShouldBe(2);
        collector.ContainersByState.ShouldContainKey("Exited");
        collector.ContainersByState["Exited"].ShouldBe(1);
    }

    // -----------------------------------------------------------------------
    // Host metrics (basic validation — values depend on platform)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CollectMetrics_HostMetrics_ArePopulated()
    {
        _instanceStore.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ManagedInstance>().AsReadOnly());
        _containerManager.ListAsync(true, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ContainerInfo>().AsReadOnly());

        using var collector = CreateCollector();
        await collector.CollectMetricsAsync();

        collector.MemoryTotalBytes.ShouldBeGreaterThan(0);
        collector.MemoryAvailableBytes.ShouldBeGreaterThanOrEqualTo(0);
        collector.MemoryUsagePercent.ShouldBeInRange(0, 100);
        collector.CpuUsagePercent.ShouldBeGreaterThanOrEqualTo(0);
    }

    // -----------------------------------------------------------------------
    // Error resilience
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CollectMetrics_ContainerManagerThrows_DoesNotCrash()
    {
        _instanceStore.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ManagedInstance>
            {
                new() { Id = "a", DesiredState = DesiredState.Running, DesiredReplicas = 1, ProviderName = "Docker", ContainerIds = ["c1"] }
            }.AsReadOnly());

        _containerManager.ListAsync(true, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Docker not available"));

        using var collector = CreateCollector();
        // Should not throw
        await collector.CollectMetricsAsync();

        // Instance count should still be populated from the store
        collector.TotalManagedInstances.ShouldBe(1);
    }

    [Fact]
    public async Task CollectMetrics_InstanceStoreThrows_DoesNotCrash()
    {
        _instanceStore.GetAllAsync(Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Store unavailable"));

        using var collector = CreateCollector();
        // Should not throw
        await collector.CollectMetricsAsync();
    }

    // -----------------------------------------------------------------------
    // Image parsing
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("nginx:1.25", "nginx", "1.25")]
    [InlineData("nginx", "nginx", "latest")]
    [InlineData("myregistry.io/app:v2.0", "myregistry.io/app", "v2.0")]
    [InlineData("localhost:5000/myapp:1.0", "localhost:5000/myapp", "1.0")]
    [InlineData("myapp@sha256:abc123", "myapp", "sha256:abc123")]
    [InlineData("", "unknown", "unknown")]
    [InlineData("mcr.microsoft.com/dotnet/aspnet:10.0", "mcr.microsoft.com/dotnet/aspnet", "10.0")]
    public void ParseImageAndVersion_ParsesCorrectly(string imageRef, string expectedName, string expectedVersion)
    {
        OrchestratorMetricsCollector.ParseImageAndVersion(imageRef, out var name, out var version);

        name.ShouldBe(expectedName);
        version.ShouldBe(expectedVersion);
    }

    // -----------------------------------------------------------------------
    // Start/Stop lifecycle
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_StopAsync_DoNotThrow()
    {
        _instanceStore.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ManagedInstance>().AsReadOnly());
        _containerManager.ListAsync(true, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ContainerInfo>().AsReadOnly());

        using var collector = CreateCollector();

        await collector.StartAsync(CancellationToken.None);
        // Give the timer a moment to fire at least once
        await Task.Delay(100);
        await collector.StopAsync(CancellationToken.None);
    }

    // -----------------------------------------------------------------------
    // Instance with no container IDs is unhealthy
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CollectMetrics_RunningInstanceWithNoContainerIds_IsUnhealthy()
    {
        var instances = new List<ManagedInstance>
        {
            new()
            {
                Id = "inst-1",
                ContainerIds = [],
                DesiredState = DesiredState.Running,
                DesiredReplicas = 1,
                ProviderName = "Docker"
            }
        };

        _instanceStore.GetAllAsync(Arg.Any<CancellationToken>()).Returns(instances.AsReadOnly());
        _containerManager.ListAsync(true, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ContainerInfo>().AsReadOnly());

        using var collector = CreateCollector();
        await collector.CollectMetricsAsync();

        collector.HealthyInstances.ShouldBe(0);
        collector.InstancesByState.ShouldContainKey("unhealthy");
        collector.InstancesByState["unhealthy"].ShouldBe(1);
    }
}
