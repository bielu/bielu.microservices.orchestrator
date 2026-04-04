using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Containerd;
using Bielu.Microservices.Orchestrator.Containerd.Extensions;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Kubernetes.Extensions;
using Bielu.Microservices.Orchestrator.Podman.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for multiple provider registrations.
/// </summary>
public class ProviderRegistrationTests
{
    [Fact]
    public void AddPodman_RegistersWithCorrectProviderName()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMicroservicesOrchestrator(builder =>
        {
            builder.AddPodman(options =>
            {
                options.Endpoint = "unix:///run/podman/podman.sock";
            });
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetService<IContainerOrchestrator>();
        orchestrator.ShouldNotBeNull();
        orchestrator.ProviderName.ShouldBe("Podman");
    }

    [Fact]
    public void AddContainerd_RegistersWithCorrectProviderName()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMicroservicesOrchestrator(builder =>
        {
            builder.AddContainerd(options =>
            {
                options.Endpoint = "http://localhost:1234";
                options.Namespace = "test";
            });
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetService<IContainerOrchestrator>();
        orchestrator.ShouldNotBeNull();
        orchestrator.ProviderName.ShouldBe("Containerd");
    }

    [Fact]
    public void AddKubernetes_RegistersWithCorrectProviderName()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMicroservicesOrchestrator(builder =>
        {
            builder.AddKubernetes(options =>
            {
                options.Namespace = "test-namespace";
                options.ApiServerUrl = "https://localhost:6443";
            });
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetService<IContainerOrchestrator>();
        orchestrator.ShouldNotBeNull();
        orchestrator.ProviderName.ShouldBe("Kubernetes");
    }
}
