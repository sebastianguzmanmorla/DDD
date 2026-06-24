using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;

namespace SebastianGuzmanMorla.DDD.Infrastructure.Repositories;

public sealed class UnitOfWork<TContext>(
    TContext context
) : IUnitOfWork<TContext>
    where TContext : DbContext
{
    private readonly List<Func<Task>> _postCommitActions = [];
    private IDbContextTransaction? _transaction;

    public TContext Context => context;
    public bool TransactionEnabled => _transaction is not null;

    public async Task CreateTransaction(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("Transaction already started");
        }

        _transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task RegisterPostCommitAction(Func<Task> action)
    {
        if (!TransactionEnabled)
        {
            await action();
            return;
        }

        _postCommitActions.Add(action);
    }

    public async Task Commit(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException("No active transaction");
        }

        await context.SaveChangesAsync(cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        await _transaction.DisposeAsync();
        _transaction = null;
        context.ChangeTracker.Clear();

        foreach (Func<Task> action in _postCommitActions)
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error ejecutando PostCommitAction: {ex.Message}");
            }

        _postCommitActions.Clear();
    }

    public async Task Rollback(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        context.ChangeTracker.Clear();
        _postCommitActions.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public void Dispose()
    {
        _transaction?.Rollback();
        _transaction?.Dispose();
        _transaction = null;
    }
}
