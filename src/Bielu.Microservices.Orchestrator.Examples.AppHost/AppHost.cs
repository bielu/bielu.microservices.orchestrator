var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var orchestratorDb = postgres.AddDatabase("orchestratordb");

// Build the example worker Docker image
var worker = builder.AddDockerfile("example-worker", "../Bielu.Microservices.Orchestrator.Examples.Worker")
    .WithBuildArg("CONFIGURATION", "Release");

var api = builder.AddProject<Projects.Bielu_Microservices_Orchestrator_Examples_Api>("api")
    .WithReference(orchestratorDb)
    .WaitFor(orchestratorDb);

builder.Build().Run();
