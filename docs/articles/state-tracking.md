# State Tracking & Recovery

With state tracking enabled, every create, remove and scale operation is persisted to an <xref:Bielu.Microservices.Orchestrator.Abstractions.IInstanceStore>. On startup, the `InstanceRecoveryService` hosted service reconciles what the store says should be running with what the runtime actually has.

```csharp
builder.Services.AddMicroservicesOrchestrator(orchestrator => orchestrator
    .AddDocker()
    .UseFileInstanceStore(o => o.FilePath = "/var/lib/myapp/orchestrator-state.json")
    .WithStateTracking());
```

The state-tracking decorator always wraps closest to the provider, regardless of where `WithStateTracking()` appears relative to other decorators such as OpenTelemetry.

## Instance stores

Register exactly one store; registering another replaces the previous one.

### In-memory (default)

```csharp
orchestrator.UseInMemoryInstanceStore();
```

Does not persist anything across restarts. Useful for tests and short-lived processes.

### File (JSON)

```bash
dotnet add package Bielu.Microservices.Orchestrator.Storage.File
```

```csharp
orchestrator.UseFileInstanceStore(options =>
{
    options.FilePath = "orchestrator-state.json"; // default
});
```

### EF Core

```bash
dotnet add package Bielu.Microservices.Orchestrator.Storage.EfCore
```

```csharp
orchestrator.UseEfCoreInstanceStore(options =>
    options.UseSqlite("Data Source=orchestrator.db"));
```

Any EF Core database provider works (`UseSqlServer`, `UseNpgsql`, ...).

## Custom stores

Implement <xref:Bielu.Microservices.Orchestrator.Abstractions.IInstanceStore> and register it in place of the default in-memory store, the same way the built-in `Use*InstanceStore()` methods do:

```csharp
orchestrator.Services.RemoveAll<IInstanceStore>();
orchestrator.Services.AddSingleton<IInstanceStore, MyInstanceStore>();
orchestrator.WithStateTracking();
```
