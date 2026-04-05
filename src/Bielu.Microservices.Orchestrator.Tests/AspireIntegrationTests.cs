using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Integration tests using Aspire testing infrastructure.
/// These tests require Docker to be available on the host.
/// </summary>
public class AspireIntegrationTests
{
    [Fact]
    public async Task AppHost_BuildsSuccessfully()
    {
        // Arrange & Act
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Bielu_Microservices_Orchestrator_Examples_AppHost>();

        await using var app = await appHost.BuildAsync();

        // Assert - the app should build without errors
        app.ShouldNotBeNull();
    }

    [Fact]
    public async Task AppHost_CreatesHttpClient_ForApiResource()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Bielu_Microservices_Orchestrator_Examples_AppHost>();

        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        // Act
        var httpClient = app.CreateHttpClient("api");

        // Assert
        httpClient.ShouldNotBeNull();
    }
}
