using Aspire.Hosting.Testing;
using Shouldly;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Integration tests using Aspire testing infrastructure.
/// </summary>
public class AspireIntegrationTests
{
    [Fact]
    public async Task AppHost_StartsSuccessfully()
    {
        // Arrange & Act
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Bielu_Microservices_Orchestrator_Examples_AppHost>();

        await using var app = await appHost.BuildAsync();
        var resourceNotificationService = app.Services.GetRequiredService<ResourceNotificationService>();
        await app.StartAsync();

        // Assert - verify the API resource is available
        await resourceNotificationService.WaitForResourceAsync("api", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Api_ProviderEndpoint_ReturnsDockerProvider()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Bielu_Microservices_Orchestrator_Examples_AppHost>();

        await using var app = await appHost.BuildAsync();
        var resourceNotificationService = app.Services.GetRequiredService<ResourceNotificationService>();
        await app.StartAsync();

        // Act
        var httpClient = app.CreateHttpClient("api");
        await resourceNotificationService.WaitForResourceAsync("api", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromSeconds(30));

        var response = await httpClient.GetAsync("/api/provider");

        // Assert
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }
}
