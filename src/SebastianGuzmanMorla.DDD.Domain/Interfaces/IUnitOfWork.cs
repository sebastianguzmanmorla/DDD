namespace SebastianGuzmanMorla.DDD.Domain.Interfaces;

public interface IUnitOfWork<out TContext> : IAsyncDisposable, IDisposable where TContext : class
{
    TContext Context { get; }

    bool TransactionEnabled { get; }

    Task RegisterPostCommitAction(Func<Task> action);

    Task CreateTransaction(CancellationToken cancellationToken = default);

    Task Commit(CancellationToken cancellationToken = default);

    Task Rollback(CancellationToken cancellationToken = default);
}
