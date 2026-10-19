using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Models;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

public class VolumeManagerExtensionsTests
{
    private readonly IVolumeManager _manager = Substitute.For<IVolumeManager>();

    [Fact]
    public async Task CreateLocalBoundAsync_CallsCreateAsync_WithLocalDriver()
    {
        _manager.CreateAsync(default!, default, default, default)
            .ReturnsForAnyArgs(Task.FromResult(new VolumeInfo { Name = "my-vol" }));

        await _manager.CreateLocalBoundAsync("my-vol", "/host/data");

        await _manager.Received(1).CreateAsync(
            "my-vol",
            "local",
            Arg.Is<IDictionary<string, string>>(d =>
                d["type"] == "none" &&
                d["o"] == "bind" &&
                d["device"] == "/host/data"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateLocalBoundAsync_BindAndReadOnly_PassedThrough()
    {
        _manager.CreateAsync(default!, default, default, default)
            .ReturnsForAnyArgs(Task.FromResult(new VolumeInfo { Name = "my-vol" }));

        await _manager.CreateLocalBoundAsync("my-vol", "/host/data", LocalMountOptions.Bind | LocalMountOptions.ReadOnly);

        await _manager.Received(1).CreateAsync(
            "my-vol",
            "local",
            Arg.Is<IDictionary<string, string>>(d => d["o"] == "bind,ro"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateLocalBoundAsync_NullManager_Throws()
    {
        IVolumeManager? nullManager = null;
        await Should.ThrowAsync<ArgumentNullException>(
            () => nullManager!.CreateLocalBoundAsync("vol", "/data"));
    }

    [Fact]
    public async Task CreateLocalBoundAsync_EmptyName_Throws()
    {
        await Should.ThrowAsync<ArgumentException>(
            () => _manager.CreateLocalBoundAsync("", "/data"));
    }

    [Fact]
    public async Task CreateLocalBoundAsync_EmptyHostPath_Throws()
    {
        await Should.ThrowAsync<ArgumentException>(
            () => _manager.CreateLocalBoundAsync("vol", ""));
    }
}
