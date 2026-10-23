---
"bielu-microservices-orchestrator": minor
---

Add a YARP-based gateway for running multiple orchestrator instances behind one endpoint. New packages: `Bielu.Microservices.Orchestrator.Gateway` (reverse proxy with API-key-protected registration, TTL expiry and least-loaded routing), `Bielu.Microservices.Orchestrator.Gateway.Registration` (`AddGatewayRegistration()` so instances register, heartbeat and deregister automatically) and `Bielu.Microservices.Orchestrator.Gateway.Contracts` (shared models).
