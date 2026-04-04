var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.Bielu_Microservices_Orchestrator_Examples_Api>("api");

builder.Build().Run();
