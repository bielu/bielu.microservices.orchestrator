using Bielu.Microservices.Orchestrator.Abstractions;
using Bielu.Microservices.Orchestrator.Models;
using Microsoft.EntityFrameworkCore;

namespace Bielu.Microservices.Orchestrator.Storage.EfCore;

/// <summary>
/// Entity Framework Core implementation of <see cref="IInstanceStore"/>.
/// Supports any database provider configured on the <see cref="InstanceStoreDbContext"/>
/// (SQL Server, PostgreSQL, SQLite, etc.).
/// </summary>
public class EfCoreInstanceStore(IDbContextFactory<InstanceStoreDbContext> factory) : IInstanceStore
{
    /// <inheritdoc />
    public async Task SaveAsync(ManagedInstance instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(instance.Id);
        await using var dbContext = await factory.CreateDbContextAsync(cancellationToken);

        instance.UpdatedAt = DateTimeOffset.UtcNow;

        var existing = await dbContext.ManagedInstances
            .FindAsync([instance.Id], cancellationToken);

        if (existing != null)
        {
            existing.UpdateFromDomainModel(instance);
        }
        else
        {
            dbContext.ManagedInstances.Add(ManagedInstanceEntity.FromDomainModel(instance));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ManagedInstance?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await using var dbContext = await factory.CreateDbContextAsync(cancellationToken);


        var entity = await dbContext.ManagedInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        return entity?.ToDomainModel();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ManagedInstance>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await factory.CreateDbContextAsync(cancellationToken);

        var entities = await dbContext.ManagedInstances
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToDomainModel()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await using var dbContext = await factory.CreateDbContextAsync(cancellationToken);

        var entity = await dbContext.ManagedInstances
            .FindAsync([id], cancellationToken);

        if (entity != null)
        {
            dbContext.ManagedInstances.Remove(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task UpdateContainerIdsAsync(string id, IReadOnlyList<string> containerIds, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(containerIds);
        await using var dbContext = await factory.CreateDbContextAsync(cancellationToken);

        var entity = await dbContext.ManagedInstances
            .FindAsync([id], cancellationToken);

        if (entity != null)
        {
            entity.ContainerIds = containerIds.ToList();
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
