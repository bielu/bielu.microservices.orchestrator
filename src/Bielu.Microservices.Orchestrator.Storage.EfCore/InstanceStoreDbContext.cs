using System.Text.Json;
using Bielu.Microservices.Orchestrator.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Bielu.Microservices.Orchestrator.Storage.EfCore;

/// <summary>
/// Entity Framework Core <see cref="DbContext"/> for persisting orchestrator instance state.
/// Configure the underlying database provider via <see cref="DbContextOptions{TContext}"/>
/// (e.g. SQL Server, PostgreSQL, SQLite).
/// </summary>
public class InstanceStoreDbContext(DbContextOptions<InstanceStoreDbContext> options) : DbContext(options)
{
    /// <summary>
    /// The set of managed instances.
    /// </summary>
    public DbSet<ManagedInstanceEntity> ManagedInstances => Set<ManagedInstanceEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ManagedInstanceEntity>(entity =>
        {
            entity.ToTable("ManagedInstances");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasMaxLength(256);
            entity.Property(e => e.ProviderName).HasMaxLength(128);
            entity.Property(e => e.DesiredState).HasConversion<string>().HasMaxLength(32);

            // Store complex types as JSON columns
            entity.Property(e => e.ContainerIdsJson).HasColumnName("ContainerIds");
            entity.Property(e => e.OriginalRequestJson).HasColumnName("OriginalRequest");
            entity.Property(e => e.MetadataJson).HasColumnName("Metadata");

            entity.Ignore(e => e.ContainerIds);
            entity.Ignore(e => e.OriginalRequest);
            entity.Ignore(e => e.Metadata);
        });
    }
}

/// <summary>
/// EF Core entity representation of <see cref="ManagedInstance"/>.
/// Complex properties are stored as JSON strings for broad database provider compatibility.
/// </summary>
public class ManagedInstanceEntity
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Unique instance or group identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Unique instance or group identifier.
    /// </summary>
    public Guid OrchestratorId { get; set; } = Guid.Empty;
    /// <summary>
    /// JSON-serialized list of runtime container IDs.
    /// </summary>
    public string ContainerIdsJson { get; set; } = "[]";

    /// <summary>
    /// JSON-serialized original creation request.
    /// </summary>
    public string OriginalRequestJson { get; set; } = "{}";

    /// <summary>
    /// What the user wants: Running, Stopped, or Removed.
    /// </summary>
    public DesiredState DesiredState { get; set; } = DesiredState.Running;

    /// <summary>
    /// Target replica count.
    /// </summary>
    public int DesiredReplicas { get; set; } = 1;

    /// <summary>
    /// The name of the provider that manages this instance.
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// When this record was first created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When this record was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// JSON-serialized metadata dictionary.
    /// </summary>
    public string MetadataJson { get; set; } = "{}";

    // -----------------------------------------------------------------------
    // Convenience accessors (not mapped to columns)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Deserialized container IDs. Changes must be written back via <see cref="ContainerIdsJson"/>.
    /// </summary>
    public IList<string> ContainerIds
    {
        get => JsonSerializer.Deserialize<List<string>>(ContainerIdsJson, JsonOptions) ?? [];
        set => ContainerIdsJson = JsonSerializer.Serialize(value, JsonOptions);
    }

    /// <summary>
    /// Deserialized original request. Changes must be written back via <see cref="OriginalRequestJson"/>.
    /// </summary>
    public CreateContainerRequest OriginalRequest
    {
        get => JsonSerializer.Deserialize<CreateContainerRequest>(OriginalRequestJson, JsonOptions) ?? new();
        set => OriginalRequestJson = JsonSerializer.Serialize(value, JsonOptions);
    }

    /// <summary>
    /// Deserialized metadata. Changes must be written back via <see cref="MetadataJson"/>.
    /// </summary>
    public IDictionary<string, string> Metadata
    {
        get => JsonSerializer.Deserialize<Dictionary<string, string>>(MetadataJson, JsonOptions) ?? new();
        set => MetadataJson = JsonSerializer.Serialize(value, JsonOptions);
    }

    /// <summary>
    /// Converts this entity to the domain model.
    /// </summary>
    public ManagedInstance ToDomainModel() => new()
    {
        Id = Id,
        OrchestratorId = OrchestratorId,
        ContainerIds = ContainerIds,
        OriginalRequest = OriginalRequest,
        DesiredState = DesiredState,
        DesiredReplicas = DesiredReplicas,
        ProviderName = ProviderName,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        Metadata = Metadata
    };

    /// <summary>
    /// Creates an entity from the domain model.
    /// </summary>
    public static ManagedInstanceEntity FromDomainModel(ManagedInstance instance) => new()
    {
        Id = instance.Id,
        ContainerIds = instance.ContainerIds,
        OriginalRequest = instance.OriginalRequest,
        DesiredState = instance.DesiredState,
        DesiredReplicas = instance.DesiredReplicas,
        ProviderName = instance.ProviderName,
        CreatedAt = instance.CreatedAt,
        UpdatedAt = instance.UpdatedAt,
        Metadata = instance.Metadata
    };

    /// <summary>
    /// Updates this entity from the domain model.
    /// </summary>
    public void UpdateFromDomainModel(ManagedInstance instance)
    {
        ContainerIds = instance.ContainerIds;
        OriginalRequest = instance.OriginalRequest;
        DesiredState = instance.DesiredState;
        DesiredReplicas = instance.DesiredReplicas;
        ProviderName = instance.ProviderName;
        UpdatedAt = instance.UpdatedAt;
        Metadata = instance.Metadata;
    }
}
