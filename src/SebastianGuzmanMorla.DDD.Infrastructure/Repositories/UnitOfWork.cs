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
                Console.WriteLine($"Error executing PostCommitAction: {ex.Message}");
            }

        _postCommitActions.Clear();
    }

    public async Task Rollback(CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? transaction = _transaction;
        try
        {
            if (transaction is not null)
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                finally
                {
                    await transaction.DisposeAsync();
                }
            }
        }
        finally
        {
            _transaction = null;
            _postCommitActions.Clear();
            context.ChangeTracker.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await Rollback();
        }
        else
        {
            _postCommitActions.Clear();
        }
    }

    public void Dispose()
    {
        // A DI scope may dispose an unused context. Do not initialize EF services here.
        if (_transaction is null)
        {
            _postCommitActions.Clear();
            return;
        }

        try
        {
            try
            {
                _transaction?.Rollback();
            }
            finally
            {
                _transaction?.Dispose();
            }
        }
        finally
        {
            _transaction = null;
            _postCommitActions.Clear();
            context.ChangeTracker.Clear();
        }
    }
}
