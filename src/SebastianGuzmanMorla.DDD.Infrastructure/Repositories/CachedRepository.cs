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

    protected virtual TimeSpan CacheExpiry => TimeSpan.FromMinutes(10);

    protected abstract JsonTypeInfo<TEntity> JsonTypeInfo { get; }

    protected string GetKey(Guid id)
    {
        return $"{CacheKeyPrefix}:{id}";
    }

    public override async Task<bool> Any(Guid id, CancellationToken cancellationToken = default)
    {
        if (UnitOfWork.TransactionEnabled)
        {
            return await base.Any(id, cancellationToken);
        }

        if (await Cache.KeyExistsAsync(GetKey(id)))
        {
            return true;
        }

        return await base.Any(id, cancellationToken);
    }

    public override async Task<TEntity?> FirstOrDefault(Guid id, CancellationToken cancellationToken = default)
    {
        // Shared cache must neither override transaction reads nor expose uncommitted rows.
        if (UnitOfWork.TransactionEnabled)
        {
            return await base.FirstOrDefault(id, cancellationToken);
        }

        RedisValue cachedValue = await Cache.StringGetAsync(GetKey(id));

        if (!cachedValue.IsNullOrEmpty)
        {
            return JsonSerializer.Deserialize(cachedValue.ToString(), JsonTypeInfo);
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

    public override async Task Add(CancellationToken cancellationToken = default, params IEnumerable<TEntity> items)
    {
        List<TEntity> entities = [.. items];
        await base.Add(cancellationToken, entities);

        await UnitOfWork.RegisterPostCommitAction(async () =>
        {
            foreach (TEntity item in entities)
            {
                string json = JsonSerializer.Serialize(item, JsonTypeInfo);

                await Cache.StringSetAsync(GetKey(item.Id), json, CacheExpiry);
            }
        });
    }

    public override async Task Update(CancellationToken cancellationToken = default, params IEnumerable<TEntity> items)
    {
        List<TEntity> entities = [.. items];
        await base.Update(cancellationToken, entities);

        await UnitOfWork.RegisterPostCommitAction(() => InvalidateCache(entities));
    }

    public override async Task<int> Upsert(CancellationToken cancellationToken = default,
        params IEnumerable<TEntity> items)
    {
        List<TEntity> entities = [.. items];
        int result = await base.Upsert(cancellationToken, entities);

        await UnitOfWork.RegisterPostCommitAction(() => InvalidateCache(entities));

        return result;
    }

    public override async Task SoftDelete(CancellationToken cancellationToken = default,
        params IEnumerable<TEntity> items)
    {
        List<TEntity> entities = [.. items];
        await base.SoftDelete(cancellationToken, entities);

        await UnitOfWork.RegisterPostCommitAction(() => InvalidateCache(entities));
    }

    public override async Task<int> HardDelete(CancellationToken cancellationToken = default,
        params IEnumerable<TEntity> items)
    {
        List<TEntity> entities = [.. items];
        int result = await base.HardDelete(cancellationToken, entities);

        await UnitOfWork.RegisterPostCommitAction(() => InvalidateCache(entities));

        return result;
    }

    private async Task InvalidateCache(IEnumerable<TEntity> items)
    {
        RedisKey[] keys = items.Select(x => (RedisKey)GetKey(x.Id)).ToArray();

        if (keys.Length != 0)
        {
            await Cache.KeyDeleteAsync(keys);
        }
    }
}
