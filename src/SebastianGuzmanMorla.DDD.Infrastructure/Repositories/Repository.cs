using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Entities;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;

namespace SebastianGuzmanMorla.DDD.Infrastructure.Repositories;

public abstract class Repository<TContext, TEntity>(
    IServiceProvider serviceProvider
) : IRepository<TEntity>
    where TContext : DbContext
    where TEntity : Entity
{
    protected readonly IUnitOfWork<TContext> UnitOfWork = serviceProvider.GetRequiredService<IUnitOfWork<TContext>>();

    protected DbSet<TEntity> DbSet => UnitOfWork.Context.Set<TEntity>();

    protected IQueryable<TEntity> Queryable =>
        UnitOfWork.Context.Set<TEntity>().AsNoTracking().Where(x => x.DeletedAt == null);

    public virtual async Task<bool> Any(Guid id, CancellationToken cancellationToken = default)
    {
        return await Queryable
            .Where(x => x.Id == id)
            .AnyAsync(cancellationToken);
    }

    public virtual async Task<int> Count(CancellationToken cancellationToken = default)
    {
        return await Queryable.CountAsync(cancellationToken);
    }

    public virtual async Task<TEntity?> FirstOrDefault(Guid id, CancellationToken cancellationToken = default)
    {
        return await Queryable
            .Where(x => x.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public virtual async Task Add(CancellationToken cancellationToken = default, params IEnumerable<TEntity> items)
    {
        List<TEntity> enumerable = [.. items];

        foreach (TEntity item in enumerable) item.UpdatedAt = DateTime.UtcNow;

        DbSet.AddRange(enumerable);

        if (!UnitOfWork.TransactionEnabled)
        {
            await UnitOfWork.Context.SaveChangesAsync(cancellationToken);

            foreach (TEntity item in enumerable) UnitOfWork.Context.Entry(item).State = EntityState.Detached;
        }
    }

    public virtual async Task Update(CancellationToken cancellationToken = default, params IEnumerable<TEntity> items)
    {
        List<TEntity> enumerable = [.. items];

        foreach (TEntity item in enumerable)
        {
            item.UpdatedAt = DateTime.UtcNow;

            TEntity? local = DbSet.Local.FirstOrDefault(e => e.Id == item.Id);

            if (local is not null)
            {
                UnitOfWork.Context.Entry(local).CurrentValues.SetValues(item);
            }
            else
            {
                DbSet.Attach(item);
                UnitOfWork.Context.Entry(item).State = EntityState.Modified;
            }
        }

        if (!UnitOfWork.TransactionEnabled)
        {
            await UnitOfWork.Context.SaveChangesAsync(cancellationToken);

            foreach (TEntity item in enumerable) UnitOfWork.Context.Entry(item).State = EntityState.Detached;
        }
    }

    public virtual async Task<int> Upsert(CancellationToken cancellationToken = default,
        params IEnumerable<TEntity> items)
    {
        List<TEntity> enumerable = [.. items];

        foreach (TEntity item in enumerable) item.UpdatedAt = DateTime.UtcNow;

        int result = await DbSet.UpsertRange(enumerable)
            .On(x => x.Id)
            .RunAsync(cancellationToken);

        if (!UnitOfWork.TransactionEnabled)
        {
            await UnitOfWork.Context.SaveChangesAsync(cancellationToken);

            foreach (TEntity item in enumerable) UnitOfWork.Context.Entry(item).State = EntityState.Detached;
        }

        return result;
    }

    public virtual async Task SoftDelete(CancellationToken cancellationToken = default,
        params IEnumerable<TEntity> items)
    {
        List<TEntity> enumerable = [.. items];

        foreach (TEntity item in enumerable)
        {
            item.DeletedAt = DateTime.UtcNow;

            TEntity? local = DbSet.Local.FirstOrDefault(e => e.Id == item.Id);

            if (local is not null)
            {
                UnitOfWork.Context.Entry(local).CurrentValues.SetValues(item);
            }
            else
            {
                DbSet.Attach(item);
                UnitOfWork.Context.Entry(item).State = EntityState.Modified;
            }
        }

        if (!UnitOfWork.TransactionEnabled)
        {
            await UnitOfWork.Context.SaveChangesAsync(cancellationToken);

            foreach (TEntity item in enumerable) UnitOfWork.Context.Entry(item).State = EntityState.Detached;
        }
    }

    public virtual async Task<int> HardDelete(CancellationToken cancellationToken = default,
        params IEnumerable<TEntity> items)
    {
        List<TEntity> enumerable = [.. items];

        List<Guid> ids = [.. enumerable.Select(x => x.Id)];

        int result = await UnitOfWork.Context.Set<TEntity>()
            .Where(x => ids.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);

        if (!UnitOfWork.TransactionEnabled)
        {
            await UnitOfWork.Context.SaveChangesAsync(cancellationToken);

            foreach (TEntity item in enumerable) UnitOfWork.Context.Entry(item).State = EntityState.Detached;
        }

        return result;
    }
}
