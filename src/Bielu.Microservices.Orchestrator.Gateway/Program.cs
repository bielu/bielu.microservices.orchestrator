using Bielu.Microservices.Orchestrator.Gateway.Extensions;

var builder = WebApplication.CreateBuilder(args);

// -------------------------------------------------------------------------
// Orchestrator Gateway — YARP reverse proxy with dynamic registration
// -------------------------------------------------------------------------
// Orchestrator instances register themselves via POST /api/gateway/register,
// send periodic heartbeats via PUT /api/gateway/heartbeat/{id}, and
// deregister on shutdown via DELETE /api/gateway/register/{id}.
//
// All registration endpoints are secured with API key authentication.
// The gateway dynamically updates YARP destinations as instances come and go.
builder.Services.AddOrchestratorGateway(options =>
{
    options.ApiKey = builder.Configuration["Gateway:ApiKey"]
        ?? throw new InvalidOperationException("Gateway:ApiKey must be configured.");
    options.InstanceTtlSeconds = builder.Configuration.GetValue("Gateway:InstanceTtlSeconds", 30);
    options.RoutePattern = builder.Configuration.GetValue("Gateway:RoutePattern", "{**catch-all}")!;
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapReverseProxy();

app.Run();
