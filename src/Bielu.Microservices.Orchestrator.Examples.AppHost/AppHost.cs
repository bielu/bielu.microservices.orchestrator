var builder = DistributedApplication.CreateBuilder(args);
var otel = builder.AddOpenTelemetryCollector("opentelemetry-collector")
    .WithConfig("./config.yaml").WithAppForwarding();

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var orchestratorDb = postgres.AddDatabase("orchestratordb");

// Build the example worker Docker image
var worker = builder
    .AddDockerfile("example-worker", "../", "./Bielu.Microservices.Orchestrator.Examples.Worker/Dockerfile")
    .WithBuildArg("CONFIGURATION", "Release").WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel:4317")
    .WithEnvironment("OTEL_SERVICE_NAME", "myapp").WithExplicitStart();

var api = builder.AddProject<Projects.Bielu_Microservices_Orchestrator_Examples_Api>("api")
    .WithReference(orchestratorDb)
    .WaitFor(orchestratorDb);

builder.Build().Run();