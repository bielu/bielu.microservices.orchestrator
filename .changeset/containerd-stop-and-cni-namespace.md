---
"bielu-microservices-orchestrator": patch
---

containerd: `StopAsync` with a timeout now escalates to SIGKILL when the grace period expires. With default `Grpc.Net.Client` settings a cancelled call surfaces as `RpcException(StatusCode.Cancelled)`, not `OperationCanceledException`, so previously the stop failed with that exception instead of killing the task. `ConnectAsync`/`DisconnectAsync` on the containerd network manager now send the `containerd-namespace` header when resolving the task's network namespace, so they target the configured namespace like every other call.
