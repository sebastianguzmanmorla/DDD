using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Entities;
using StackExchange.Redis;

namespace SebastianGuzmanMorla.DDD.Infrastructure.Repositories;

public abstract class CachedRepository<TContext, TEntity>(
    IServiceProvider serviceProvider
) : Repository<TContext, TEntity>(serviceProvider)
    where TContext : DbContext
    where TEntity : Entity
{
    protected readonly IDatabase Cache = serviceProvider.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    protected abstract string CacheKeyPrefix { get; }

    /// <summary>
    /// Opts into shared Redis reads for repositories whose rows and fields have the same
    /// visibility for every caller sharing the cache namespace.
    /// </summary>
    protected virtual bool CacheHasUniformVisibility => false;

    protected virtual TimeSpan CacheExpiry => TimeSpan.FromMinutes(10);

    protected abstract JsonTypeInfo<TEntity> JsonTypeInfo { get; }

    protected string GetKey(Guid id)
    {
        return $"{CacheKeyPrefix}:{id}";
    }

    public override async Task<bool> Any(Guid id, CancellationToken cancellationToken = default)
    {
        if (CacheHasUniformVisibility && !UnitOfWork.TransactionEnabled && await ReadCachedEntity(id) is not null)
        {
            return true;
        }

        return await base.Any(id, cancellationToken);
    }

    public override async Task<TEntity?> FirstOrDefault(Guid id, CancellationToken cancellationToken = default)
    {
        // Shared cache must neither override transaction reads nor expose uncommitted rows.
        if (!CacheHasUniformVisibility || UnitOfWork.TransactionEnabled)
        {
            return await base.FirstOrDefault(id, cancellationToken);
        }

        TEntity? cached = await ReadCachedEntity(id);
        if (cached is not null)
        {
            return cached;
        }

        TEntity? entity = await base.FirstOrDefault(id, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        string json = JsonSerializer.Serialize(entity, JsonTypeInfo);

        await Cache.StringSetAsync(GetKey(id), json, CacheExpiry);

        return entity;
    }

    private async Task<TEntity?> ReadCachedEntity(Guid id)
    {
        RedisValue value = await Cache.StringGetAsync(GetKey(id));
        if (value.IsNullOrEmpty) return null;

        try
        {
            TEntity? entity = JsonSerializer.Deserialize(value.ToString(), JsonTypeInfo);
            return entity is { DeletedAt: null } && entity.Id == id ? entity : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public override async Task Add(CancellationToken cancellationToken = default, params IEnumerable<TEntity> items)
    {
        List<TEntity> entities = [.. items];
        await base.Add(cancellationToken, entities);

        if (!CacheHasUniformVisibility) return;

        await UnitOfWork.RegisterPostCommitAction(async () =>
        {
            foreach (TEntity item in entities)
            {
                // The committed row must pass the same query filter as an ordinary cache miss.
                TEntity? stored = await base.FirstOrDefault(item.Id);
                if (stored is not null)
                {
                    string json = JsonSerializer.Serialize(stored, JsonTypeInfo);
                    await Cache.StringSetAsync(GetKey(item.Id), json, CacheExpiry);
                }
                else
                {
                    await Cache.KeyDeleteAsync(GetKey(item.Id));
                }
            }
        });
    }

    public override async Task Update(CancellationToken cancellationToken = default, params IEnumerable<TEntity> items)
    {
        List<TEntity> entities = [.. items];
        await base.Update(cancellationToken, entities);

        await ScheduleInvalidation(entities);
    }

    public override async Task<int> Upsert(CancellationToken cancellationToken = default,
        params IEnumerable<TEntity> items)
    {
        List<TEntity> entities = [.. items];
        int result = await base.Upsert(cancellationToken, entities);

        await ScheduleInvalidation(entities);

        return result;
    }

    public override async Task SoftDelete(CancellationToken cancellationToken = default,
        params IEnumerable<TEntity> items)
    {
        List<TEntity> entities = [.. items];
        await base.SoftDelete(cancellationToken, entities);

        await ScheduleInvalidation(entities);
    }

    public override async Task<int> HardDelete(CancellationToken cancellationToken = default,
        params IEnumerable<TEntity> items)
    {
        List<TEntity> entities = [.. items];
        int result = await base.HardDelete(cancellationToken, entities);

        await ScheduleInvalidation(entities);

        return result;
    }

    private Task ScheduleInvalidation(List<TEntity> entities) =>
        CacheHasUniformVisibility
            ? UnitOfWork.RegisterPostCommitAction(() => InvalidateCache(entities))
            : Task.CompletedTask;

    private async Task InvalidateCache(IEnumerable<TEntity> items)
    {
        RedisKey[] keys = items.Select(x => (RedisKey)GetKey(x.Id)).ToArray();

        if (keys.Length != 0)
        {
            await Cache.KeyDeleteAsync(keys);
        }
    }
}
