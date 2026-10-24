using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Models;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for <see cref="VolumeMount"/> and the <c>WithVolume</c> request extensions.
/// </summary>
public class VolumeMountTests
{
    [Fact]
    public void Parse_SinglePart_UsesItForBothPaths()
    {
        // Act
        var mount = VolumeMount.Parse("/data");

        // Assert
        mount.HostPath.ShouldBe("/data");
        mount.ContainerPath.ShouldBe("/data");
        mount.ReadOnly.ShouldBeFalse();
    }

    [Fact]
    public void Parse_HostAndContainer_SetsBothPaths()
    {
        // Act
        var mount = VolumeMount.Parse("myvolume:/var/lib/data");

        // Assert
        mount.HostPath.ShouldBe("myvolume");
        mount.ContainerPath.ShouldBe("/var/lib/data");
        mount.ReadOnly.ShouldBeFalse();
    }

    [Theory]
    [InlineData("ro", true)]
    [InlineData("RO", true)]
    [InlineData("rw", false)]
    public void Parse_WithMode_SetsReadOnly(string mode, bool expected)
    {
        // Act
        var mount = VolumeMount.Parse($"/host:/container:{mode}");

        // Assert
        mount.HostPath.ShouldBe("/host");
        mount.ContainerPath.ShouldBe("/container");
        mount.ReadOnly.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_NullOrWhiteSpace_Throws(string? binding)
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => VolumeMount.Parse(binding!)).ParamName.ShouldBe("binding");
    }

    [Fact]
    public void Parse_TooManyParts_Throws()
    {
        // Act & Assert
        var ex = Should.Throw<ArgumentException>(() => VolumeMount.Parse("a:b:ro:extra"));
        ex.Message.ShouldContain("a:b:ro:extra");
    }

    [Theory]
    [InlineData("/data", true)]
    [InlineData("./data", true)]
    [InlineData(".\\data", true)]
    [InlineData("myvolume", false)]
    public void IsBindMount_DependsOnHostPathShape(string hostPath, bool expected)
    {
        // Arrange
        var mount = new VolumeMount { HostPath = hostPath, ContainerPath = "/c" };

        // Act & Assert
        mount.IsBindMount.ShouldBe(expected);
    }

    [Fact]
    public void ToBindString_ReadWrite_OmitsMode()
    {
        // Arrange
        var mount = new VolumeMount { HostPath = "vol", ContainerPath = "/data" };

        // Act & Assert
        mount.ToBindString().ShouldBe("vol:/data");
        mount.ToString().ShouldBe("vol:/data");
    }

    [Fact]
    public void ToBindString_ReadOnly_AppendsRo()
    {
        // Arrange
        var mount = new VolumeMount { HostPath = "/host", ContainerPath = "/data", ReadOnly = true };

        // Act & Assert
        mount.ToBindString().ShouldBe("/host:/data:ro");
    }

    [Fact]
    public void ToBindString_RoundTripsThroughParse()
    {
        // Arrange
        var original = new VolumeMount { HostPath = "/host", ContainerPath = "/data", ReadOnly = true };

        // Act
        var parsed = VolumeMount.Parse(original.ToBindString());

        // Assert
        parsed.HostPath.ShouldBe(original.HostPath);
        parsed.ContainerPath.ShouldBe(original.ContainerPath);
        parsed.ReadOnly.ShouldBeTrue();
    }

    [Fact]
    public void WithVolume_HostAndContainerPaths_AddsMount()
    {
        // Arrange
        var request = new CreateContainerRequest();

        // Act
        var result = request.WithVolume("/host", "/data", readOnly: true);

        // Assert
        result.ShouldBeSameAs(request);
        var mount = request.Volumes.ShouldHaveSingleItem();
        mount.HostPath.ShouldBe("/host");
        mount.ContainerPath.ShouldBe("/data");
        mount.ReadOnly.ShouldBeTrue();
    }

    [Fact]
    public void WithVolume_StructuredMount_AddsTheSameInstance()
    {
        // Arrange
        var request = new CreateContainerRequest();
        var mount = new VolumeMount { HostPath = "vol", ContainerPath = "/data" };

        // Act
        request.WithVolume(mount);

        // Assert
        request.Volumes.ShouldHaveSingleItem().ShouldBeSameAs(mount);
    }

    [Fact]
    public void WithVolume_NullStructuredMount_Throws()
    {
        // Arrange
        var request = new CreateContainerRequest();

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => request.WithVolume((VolumeMount)null!));
    }

    [Fact]
    public void LocalVolumeOptions_ToDictionary_NoFlags_DefaultsToBind()
    {
        // Arrange
        var options = new LocalVolumeOptions { Device = "/srv", MountOptions = 0 };

        // Act
        var dictionary = options.ToDictionary();

        // Assert
        dictionary["o"].ShouldBe("bind");
        dictionary["device"].ShouldBe("/srv");
    }

    [Fact]
    public void LocalVolumeOptions_ToDictionary_AllFlags_ListsEveryOption()
    {
        // Arrange
        var options = new LocalVolumeOptions
        {
            Device = "/srv",
            MountOptions = LocalMountOptions.Bind | LocalMountOptions.ReadOnly | LocalMountOptions.Suid
                           | LocalMountOptions.NoExec | LocalMountOptions.NoAtime
        };

        // Act
        var dictionary = options.ToDictionary();

        // Assert
        dictionary["o"].ShouldBe("bind,ro,suid,noexec,noatime");
    }
}
