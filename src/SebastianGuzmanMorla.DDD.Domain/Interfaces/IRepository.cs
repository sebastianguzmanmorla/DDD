using SebastianGuzmanMorla.DDD.Domain.Entities;

namespace SebastianGuzmanMorla.DDD.Domain.Interfaces;

public interface IRepository<TEntity>
    where TEntity : Entity
{
    Task<bool> Any(Guid id, CancellationToken cancellationToken = default);
    Task<int> Count(CancellationToken cancellationToken = default);
    Task<TEntity?> FirstOrDefault(Guid id, CancellationToken cancellationToken = default);
    Task Add(CancellationToken cancellationToken = default, params IEnumerable<TEntity> items);
    Task Update(CancellationToken cancellationToken = default, params IEnumerable<TEntity> items);
    Task<int> Upsert(CancellationToken cancellationToken = default, params IEnumerable<TEntity> items);
    Task SoftDelete(CancellationToken cancellationToken = default, params IEnumerable<TEntity> items);
    Task<int> HardDelete(CancellationToken cancellationToken = default, params IEnumerable<TEntity> items);
}
