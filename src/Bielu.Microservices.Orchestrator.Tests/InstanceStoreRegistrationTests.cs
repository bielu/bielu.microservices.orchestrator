using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Configuration;
using Bielu.Microservices.Orchestrator.Extensions;
using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Storage;
using Bielu.Microservices.Orchestrator.Storage.File;
using Bielu.Microservices.Orchestrator.Storage.File.Configuration;
using Bielu.Microservices.Orchestrator.Storage.File.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for instance store DI registration and builder extensions.
/// </summary>
public class InstanceStoreRegistrationTests
{
    [Fact]
    public void DefaultRegistration_RegistersInMemoryInstanceStore()
    {
        var services = new ServiceCollection();

        services.AddMicroservicesOrchestrator();

        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IInstanceStore>();
        store.ShouldNotBeNull();
        store.ShouldBeOfType<InMemoryInstanceStore>();
    }

    [Fact]
    public void UseInMemoryInstanceStore_ExplicitlyRegisters()
    {
        var services = new ServiceCollection();

        services.AddMicroservicesOrchestrator(builder =>
        {
            builder.UseInMemoryInstanceStore();
        });

        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IInstanceStore>();
        store.ShouldNotBeNull();
        store.ShouldBeOfType<InMemoryInstanceStore>();
    }

    [Fact]
    public void UseFileInstanceStore_RegistersFileBasedStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMicroservicesOrchestrator(builder =>
        {
            builder.UseFileInstanceStore(opts =>
            {
                opts.FilePath = "/tmp/test-state.json";
            });
        });

        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IInstanceStore>();
        store.ShouldNotBeNull();
        store.ShouldBeOfType<FileBasedInstanceStore>();
    }

    [Fact]
    public void UseFileInstanceStore_RegistersOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMicroservicesOrchestrator(builder =>
        {
            builder.UseFileInstanceStore(opts =>
            {
                opts.FilePath = "/data/my-state.json";
            });
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetService<FileInstanceStoreOptions>();
        options.ShouldNotBeNull();
        options.FilePath.ShouldBe("/data/my-state.json");
    }

    [Fact]
    public void UseFileInstanceStore_OverridesDefaultInMemoryStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMicroservicesOrchestrator(builder =>
        {
            builder.UseFileInstanceStore();
        });

        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IInstanceStore>();
        store.ShouldNotBeNull();
        store.ShouldBeOfType<FileBasedInstanceStore>();
    }

    // -----------------------------------------------------------------------
    // Model defaults
    // -----------------------------------------------------------------------

    [Fact]
    public void ManagedInstance_DefaultValues_ShouldBeCorrect()
    {
        var instance = new ManagedInstance();

        instance.Id.ShouldBe(string.Empty);
        instance.ContainerIds.ShouldNotBeNull();
        instance.ContainerIds.ShouldBeEmpty();
        instance.OriginalRequest.ShouldNotBeNull();
        instance.DesiredState.ShouldBe(DesiredState.Running);
        instance.DesiredReplicas.ShouldBe(1);
        instance.ProviderName.ShouldBe(string.Empty);
        instance.Metadata.ShouldNotBeNull();
        instance.Metadata.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(DesiredState.Running)]
    [InlineData(DesiredState.Stopped)]
    [InlineData(DesiredState.Removed)]
    public void DesiredState_AllValues_ShouldBeDefined(DesiredState state)
    {
        Enum.IsDefined(state).ShouldBeTrue();
    }

    [Fact]
    public void FileInstanceStoreOptions_DefaultValues_ShouldBeCorrect()
    {
        var options = new FileInstanceStoreOptions();

        options.FilePath.ShouldBe("orchestrator-state.json");
    }

    // -----------------------------------------------------------------------
    // Fluent API
    // -----------------------------------------------------------------------

    [Fact]
    public void UseInMemoryInstanceStore_ReturnsBuilderForChaining()
    {
        var options = new OrchestratorOptions();
        var builder = new OrchestratorBuilder(new ServiceCollection(), options);

        var result = builder.UseInMemoryInstanceStore();

        result.ShouldBeSameAs(builder);
    }

    [Fact]
    public void UseFileInstanceStore_ReturnsBuilderForChaining()
    {
        var options = new OrchestratorOptions();
        var builder = new OrchestratorBuilder(new ServiceCollection(), options);

        var result = builder.UseFileInstanceStore();

        result.ShouldBeSameAs(builder);
    }

    [Fact]
    public void WithStateTracking_ReturnsBuilderForChaining()
    {
        var options = new OrchestratorOptions();
        var builder = new OrchestratorBuilder(new ServiceCollection(), options);

        var result = builder.WithStateTracking();

        result.ShouldBeSameAs(builder);
    }
}
