using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Docker.Extensions;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.HealthChecks;
using Bielu.Microservices.Orchestrator.HealthChecks.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for the container runtime health check.
/// </summary>
public class HealthCheckTests
{
    // -----------------------------------------------------------------------
    // DI registration
    // -----------------------------------------------------------------------

    [Fact]
    public void AddContainerRuntimeHealthCheck_RegistersCheck()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMicroservicesOrchestrator(b => b.AddDocker());
        services.AddHealthChecks()
                .AddContainerRuntimeHealthCheck();

        // Verify the check descriptor was registered
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        options.Value.Registrations.ShouldContain(r => r.Name == "container-runtime");
    }

    [Fact]
    public void AddContainerRuntimeHealthCheck_CustomName_RegistersWithThatName()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMicroservicesOrchestrator(b => b.AddDocker());
        services.AddHealthChecks()
                .AddContainerRuntimeHealthCheck(name: "my-docker");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        options.Value.Registrations.ShouldContain(r => r.Name == "my-docker");
    }

    [Fact]
    public void AddContainerRuntimeHealthCheck_DefaultTags_ContainReadyAndRuntime()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMicroservicesOrchestrator(b => b.AddDocker());
        services.AddHealthChecks()
                .AddContainerRuntimeHealthCheck();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        var registration = options.Value.Registrations.Single(r => r.Name == "container-runtime");

        registration.Tags.ShouldContain("ready");
        registration.Tags.ShouldContain("runtime");
    }

    // -----------------------------------------------------------------------
    // Health check logic
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CheckHealthAsync_WhenRuntimeAvailable_ReturnsHealthy()
    {
        var orchestrator = Substitute.For<IContainerOrchestrator>();
        orchestrator.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        orchestrator.ProviderName.Returns("Docker");

        var check = new ContainerRuntimeHealthCheck(orchestrator);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("container-runtime", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Data["provider"].ShouldBe("Docker");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenRuntimeNotAvailable_ReturnsUnhealthy()
    {
        var orchestrator = Substitute.For<IContainerOrchestrator>();
        orchestrator.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(false);
        orchestrator.ProviderName.Returns("Docker");

        var check = new ContainerRuntimeHealthCheck(orchestrator);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration(
                "container-runtime", check, HealthStatus.Unhealthy, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenExceptionThrown_ReturnsUnhealthy()
    {
        var orchestrator = Substitute.For<IContainerOrchestrator>();
        orchestrator.IsAvailableAsync(Arg.Any<CancellationToken>())
                    .Returns<bool>(_ => throw new InvalidOperationException("socket not found"));
        orchestrator.ProviderName.Returns("Docker");

        var check = new ContainerRuntimeHealthCheck(orchestrator);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration(
                "container-runtime", check, HealthStatus.Unhealthy, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Exception.ShouldNotBeNull();
        result.Exception!.Message.ShouldContain("socket not found");
    }

    [Fact]
    public async Task CheckHealthAsync_ProviderName_IsIncludedInData()
    {
        var orchestrator = Substitute.For<IContainerOrchestrator>();
        orchestrator.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        orchestrator.ProviderName.Returns("Podman");

        var check = new ContainerRuntimeHealthCheck(orchestrator);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("container-runtime", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Data.ContainsKey("provider").ShouldBeTrue();
        result.Data["provider"].ShouldBe("Podman");
    }
}
