using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Docker;
using Bielu.Microservices.Orchestrator.Docker.Extensions;
using Bielu.Microservices.Orchestrator.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for the orchestrator DI registration.
/// </summary>
public class ServiceRegistrationTests
{
    [Fact]
    public void AddMicroservicesOrchestrator_RegistersCoreServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMicroservicesOrchestrator();

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<Microsoft.Extensions.Options.IOptions<OrchestratorOptions>>();
        options.ShouldNotBeNull();
        var orchestratorOptions = provider.GetService<OrchestratorOptions>();
        orchestratorOptions.ShouldNotBeNull();
        orchestratorOptions.ManagedContainersOnly.ShouldBeTrue();
    }

    [Fact]
    public void AddMicroservicesOrchestrator_OptionsCanBeConfiguredViaBuilder()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMicroservicesOrchestrator(builder =>
        {
            builder.Options.ManagedContainersOnly = false;
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var orchestratorOptions = provider.GetService<OrchestratorOptions>();
        orchestratorOptions.ShouldNotBeNull();
        orchestratorOptions.ManagedContainersOnly.ShouldBeFalse();
    }

    [Fact]
    public void AddDocker_RegistersDockerServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMicroservicesOrchestrator(builder =>
        {
            builder.AddDocker(options =>
            {
                options.Endpoint = "unix:///var/run/docker.sock";
            });
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetService<IContainerOrchestrator>();
        orchestrator.ShouldNotBeNull();
        orchestrator.ProviderName.ShouldBe("Docker");
        orchestrator.Containers.ShouldNotBeNull();
        orchestrator.Images.ShouldNotBeNull();
        orchestrator.Networks.ShouldNotBeNull();
        orchestrator.Volumes.ShouldNotBeNull();
    }

    [Fact]
    public void AddDocker_RegistersAllManagerInterfaces()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddMicroservicesOrchestrator(builder =>
        {
            builder.AddDocker();
        });

        // Assert
        var provider = services.BuildServiceProvider();
        provider.GetService<IContainerManager>().ShouldNotBeNull();
        provider.GetService<IImageManager>().ShouldNotBeNull();
        provider.GetService<INetworkManager>().ShouldNotBeNull();
        provider.GetService<IVolumeManager>().ShouldNotBeNull();
    }
}
