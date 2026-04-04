using Bielu.Microservices.Orchestrator.Models;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for the orchestrator models.
/// </summary>
public class ModelTests
{
    [Fact]
    public void ContainerInfo_DefaultValues_ShouldBeCorrect()
    {
        var container = new ContainerInfo();

        container.Id.ShouldBe(string.Empty);
        container.Name.ShouldBe(string.Empty);
        container.Image.ShouldBe(string.Empty);
        container.State.ShouldBe(ContainerState.Unknown);
        container.Labels.ShouldNotBeNull();
        container.Ports.ShouldNotBeNull();
        container.EnvironmentVariables.ShouldNotBeNull();
    }

    [Fact]
    public void ImageInfo_DefaultValues_ShouldBeCorrect()
    {
        var image = new ImageInfo();

        image.Id.ShouldBe(string.Empty);
        image.Tags.ShouldNotBeNull();
        image.Size.ShouldBe(0);
        image.Labels.ShouldNotBeNull();
    }

    [Fact]
    public void NetworkInfo_DefaultValues_ShouldBeCorrect()
    {
        var network = new NetworkInfo();

        network.Id.ShouldBe(string.Empty);
        network.Name.ShouldBe(string.Empty);
        network.Driver.ShouldBe(string.Empty);
        network.Labels.ShouldNotBeNull();
    }

    [Fact]
    public void VolumeInfo_DefaultValues_ShouldBeCorrect()
    {
        var volume = new VolumeInfo();

        volume.Name.ShouldBe(string.Empty);
        volume.Driver.ShouldBe(string.Empty);
        volume.MountPoint.ShouldBe(string.Empty);
        volume.Labels.ShouldNotBeNull();
    }

    [Fact]
    public void PortMapping_DefaultValues_ShouldBeCorrect()
    {
        var port = new PortMapping();

        port.ContainerPort.ShouldBe(0);
        port.HostPort.ShouldBe(0);
        port.Protocol.ShouldBe("tcp");
        port.HostIp.ShouldBe("0.0.0.0");
    }

    [Fact]
    public void CreateContainerRequest_DefaultValues_ShouldBeCorrect()
    {
        var request = new CreateContainerRequest();

        request.Name.ShouldBeNull();
        request.Image.ShouldBe(string.Empty);
        request.Command.ShouldBeNull();
        request.EnvironmentVariables.ShouldNotBeNull();
        request.Ports.ShouldNotBeNull();
        request.Labels.ShouldNotBeNull();
        request.Volumes.ShouldNotBeNull();
        request.AutoRemove.ShouldBeFalse();
    }

    [Fact]
    public void PullImageRequest_DefaultValues_ShouldBeCorrect()
    {
        var request = new PullImageRequest();

        request.Image.ShouldBe(string.Empty);
        request.Tag.ShouldBe("latest");
        request.Credentials.ShouldBeNull();
    }

    [Fact]
    public void RegistryCredentials_DefaultValues_ShouldBeCorrect()
    {
        var creds = new RegistryCredentials();

        creds.ServerAddress.ShouldBe(string.Empty);
        creds.Username.ShouldBe(string.Empty);
        creds.Password.ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData(ContainerState.Unknown)]
    [InlineData(ContainerState.Created)]
    [InlineData(ContainerState.Running)]
    [InlineData(ContainerState.Paused)]
    [InlineData(ContainerState.Restarting)]
    [InlineData(ContainerState.Removing)]
    [InlineData(ContainerState.Exited)]
    [InlineData(ContainerState.Dead)]
    public void ContainerState_AllValues_ShouldBeDefined(ContainerState state)
    {
        Enum.IsDefined(state).ShouldBeTrue();
    }
}
