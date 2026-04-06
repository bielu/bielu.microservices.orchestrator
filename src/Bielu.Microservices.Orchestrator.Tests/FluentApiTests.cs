using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Models;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for the fluent API extension methods.
/// </summary>
public class FluentApiTests
{
    // -----------------------------------------------------------------------
    // OrchestratorBuilder fluent API
    // -----------------------------------------------------------------------

    [Fact]
    public void WithManagedContainersOnly_SetsOption()
    {
        var options = new OrchestratorOptions();
        var builder = new OrchestratorBuilder(new ServiceCollection(), options);

        var result = builder.WithManagedContainersOnly(false);

        result.ShouldBeSameAs(builder);
        options.ManagedContainersOnly.ShouldBeFalse();
    }

    [Fact]
    public void WithManagedContainersOnly_DefaultsToTrue()
    {
        var options = new OrchestratorOptions { ManagedContainersOnly = false };
        var builder = new OrchestratorBuilder(new ServiceCollection(), options);

        builder.WithManagedContainersOnly();

        options.ManagedContainersOnly.ShouldBeTrue();
    }

    [Fact]
    public void WithDefaultProvider_SetsOption()
    {
        var options = new OrchestratorOptions();
        var builder = new OrchestratorBuilder(new ServiceCollection(), options);

        var result = builder.WithDefaultProvider("Docker");

        result.ShouldBeSameAs(builder);
        options.DefaultProvider.ShouldBe("Docker");
    }

    [Fact]
    public void WithDefaultProvider_ThrowsOnEmpty()
    {
        var options = new OrchestratorOptions();
        var builder = new OrchestratorBuilder(new ServiceCollection(), options);

        Should.Throw<ArgumentException>(() => builder.WithDefaultProvider(""));
    }

    [Fact]
    public void Builder_FluentChaining_Works()
    {
        var options = new OrchestratorOptions();
        var builder = new OrchestratorBuilder(new ServiceCollection(), options);

        builder
            .WithManagedContainersOnly(false)
            .WithDefaultProvider("Containerd");

        options.ManagedContainersOnly.ShouldBeFalse();
        options.DefaultProvider.ShouldBe("Containerd");
    }

    // -----------------------------------------------------------------------
    // CreateContainerRequest fluent API
    // -----------------------------------------------------------------------

    [Fact]
    public void WithName_SetsName()
    {
        var request = new CreateContainerRequest().WithName("my-container");

        request.Name.ShouldBe("my-container");
    }

    [Fact]
    public void WithImage_SetsImage()
    {
        var request = new CreateContainerRequest().WithImage("nginx:latest");

        request.Image.ShouldBe("nginx:latest");
    }

    [Fact]
    public void WithCommand_SetsCommand()
    {
        var request = new CreateContainerRequest().WithCommand("sh", "-c", "echo hello");

        request.Command.ShouldNotBeNull();
        request.Command.Count.ShouldBe(3);
        request.Command[0].ShouldBe("sh");
        request.Command[1].ShouldBe("-c");
        request.Command[2].ShouldBe("echo hello");
    }

    [Fact]
    public void WithEnvironmentVariable_AddsVariable()
    {
        var request = new CreateContainerRequest()
            .WithEnvironmentVariable("KEY1", "value1")
            .WithEnvironmentVariable("KEY2", "value2");

        request.EnvironmentVariables.Count.ShouldBe(2);
        request.EnvironmentVariables["KEY1"].ShouldBe("value1");
        request.EnvironmentVariables["KEY2"].ShouldBe("value2");
    }

    [Fact]
    public void WithPort_AddsPortMapping()
    {
        var request = new CreateContainerRequest()
            .WithPort(80, 8080)
            .WithPort(443, 8443, "tcp", "127.0.0.1");

        request.Ports.Count.ShouldBe(2);
        request.Ports[0].ContainerPort.ShouldBe(80);
        request.Ports[0].HostPort.ShouldBe(8080);
        request.Ports[0].Protocol.ShouldBe("tcp");
        request.Ports[0].HostIp.ShouldBe("0.0.0.0");
        request.Ports[1].ContainerPort.ShouldBe(443);
        request.Ports[1].HostPort.ShouldBe(8443);
        request.Ports[1].HostIp.ShouldBe("127.0.0.1");
    }

    [Fact]
    public void WithLabel_AddsLabel()
    {
        var request = new CreateContainerRequest()
            .WithLabel("env", "production")
            .WithLabel("team", "platform");

        request.Labels.Count.ShouldBe(2);
        request.Labels["env"].ShouldBe("production");
        request.Labels["team"].ShouldBe("platform");
    }

    [Fact]
    public void WithVolume_AddsVolume()
    {
        var request = new CreateContainerRequest()
            .WithVolume("/host/data:/container/data")
            .WithVolume("/host/config:/container/config");

        request.Volumes.Count.ShouldBe(2);
        request.Volumes[0].ShouldBe("/host/data:/container/data");
        request.Volumes[1].ShouldBe("/host/config:/container/config");
    }

    [Fact]
    public void WithAutoRemove_SetsAutoRemove()
    {
        var request = new CreateContainerRequest().WithAutoRemove();

        request.AutoRemove.ShouldBeTrue();
    }

    [Fact]
    public void WithAutoRemove_False_DisablesAutoRemove()
    {
        var request = new CreateContainerRequest().WithAutoRemove(false);

        request.AutoRemove.ShouldBeFalse();
    }

    [Fact]
    public void WithReplicas_SetsReplicas()
    {
        var request = new CreateContainerRequest().WithReplicas(3);

        request.Replicas.ShouldBe(3);
    }

    [Fact]
    public void WithReplicas_ThrowsOnZero()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new CreateContainerRequest().WithReplicas(0));
    }

    [Fact]
    public void WithReplicas_ThrowsOnNegative()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new CreateContainerRequest().WithReplicas(-1));
    }

    [Fact]
    public void Request_FullFluentChain_Works()
    {
        var request = new CreateContainerRequest()
            .WithName("web-app")
            .WithImage("nginx:latest")
            .WithCommand("nginx", "-g", "daemon off;")
            .WithEnvironmentVariable("ENV", "prod")
            .WithPort(80, 8080)
            .WithLabel("team", "platform")
            .WithVolume("/data:/var/data")
            .WithAutoRemove()
            .WithReplicas(2);

        request.Name.ShouldBe("web-app");
        request.Image.ShouldBe("nginx:latest");
        request.Command.ShouldNotBeNull();
        request.Command.Count.ShouldBe(3);
        request.EnvironmentVariables["ENV"].ShouldBe("prod");
        request.Ports.Count.ShouldBe(1);
        request.Labels["team"].ShouldBe("platform");
        request.Volumes.Count.ShouldBe(1);
        request.AutoRemove.ShouldBeTrue();
        request.Replicas.ShouldBe(2);
    }

    [Fact]
    public void Builder_FluentApi_IntegratesWithServiceRegistration()
    {
        var services = new ServiceCollection();

        services.AddMicroservicesOrchestrator(builder =>
            builder
                .WithManagedContainersOnly(false)
                .WithDefaultProvider("Docker"));

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<OrchestratorOptions>();
        options.ManagedContainersOnly.ShouldBeFalse();
        options.DefaultProvider.ShouldBe("Docker");
    }
}
