using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Entities;
using StackExchange.Redis;

namespace SebastianGuzmanMorla.DDD.Repositories;

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
        if (await Cache.KeyExistsAsync(GetKey(id)))
        {
            return true;
        }

        return await base.Any(id, cancellationToken);
    }

    public override async Task<TEntity?> FirstOrDefault(Guid id, CancellationToken cancellationToken = default)
    {
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
        await base.Add(cancellationToken, items);

        await UnitOfWork.RegisterPostCommitAction(async () =>
        {
            foreach (TEntity item in items)
            {
                string json = JsonSerializer.Serialize(item, JsonTypeInfo);

                await Cache.StringSetAsync(GetKey(item.Id), json, CacheExpiry);
            }
        });
    }

    public override async Task Update(CancellationToken cancellationToken = default, params IEnumerable<TEntity> items)
    {
        await base.Update(cancellationToken, items);

        await UnitOfWork.RegisterPostCommitAction(() => InvalidateCache(items));
    }

    public override async Task<int> Upsert(CancellationToken cancellationToken = default,
        params IEnumerable<TEntity> items)
    {
        int result = await base.Upsert(cancellationToken, items);

        await UnitOfWork.RegisterPostCommitAction(() => InvalidateCache(items));

        return result;
    }

    public override async Task SoftDelete(CancellationToken cancellationToken = default,
        params IEnumerable<TEntity> items)
    {
        await base.SoftDelete(cancellationToken, items);

        await UnitOfWork.RegisterPostCommitAction(() => InvalidateCache(items));
    }

    public override async Task<int> HardDelete(CancellationToken cancellationToken = default,
        params IEnumerable<TEntity> items)
    {
        int result = await base.HardDelete(cancellationToken, items);

        await UnitOfWork.RegisterPostCommitAction(() => InvalidateCache(items));

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
