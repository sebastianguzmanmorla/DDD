using Microsoft.EntityFrameworkCore;

namespace SebastianGuzmanMorla.DDD.Infrastructure.Tests;

public class UnitOfWorkTests
{
    [Fact]
    public async Task Commit_PersistsChangesBeforeCallbacksAndClearsTracking()
    {
        await using var db = new DatabaseFixture();
        await db.UnitOfWork.CreateTransaction();
        await db.Repository.Add(default, new TestEntity());
        int callbacks = 0;
        await db.UnitOfWork.RegisterPostCommitAction(async () =>
        {
            Assert.False(db.UnitOfWork.TransactionEnabled);
            Assert.Empty(db.Context.ChangeTracker.Entries());
            Assert.Equal(1, await db.Context.Set<TestEntity>().CountAsync());
            callbacks++;
        });
        Assert.Equal(0, callbacks);

        await db.UnitOfWork.Commit();

        Assert.Equal(1, callbacks);
    }

    [Fact]
    public async Task Rollback_DiscardsRowsAndCallbacks()
    {
        await using var db = new DatabaseFixture();
        await db.UnitOfWork.CreateTransaction();
        await db.Repository.Add(default, new TestEntity());
        await db.Context.SaveChangesAsync();
        int callbacks = 0;
        await db.UnitOfWork.RegisterPostCommitAction(() => { callbacks++; return Task.CompletedTask; });

        await db.UnitOfWork.Rollback();
        await db.UnitOfWork.CreateTransaction();
        await db.UnitOfWork.Commit();

        Assert.Equal(0, callbacks);
        Assert.Equal(0, await db.Repository.Count());
        Assert.Empty(db.Context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_DoesNotPersistAbandonedChangesInNextTransaction(bool synchronous)
    {
        await using var db = new DatabaseFixture();
        await db.UnitOfWork.CreateTransaction();
        await db.Repository.Add(default, new TestEntity("abandoned"));

        if (synchronous) db.UnitOfWork.Dispose();
        else await db.UnitOfWork.DisposeAsync();

        await db.UnitOfWork.CreateTransaction();
        await db.Repository.Add(default, new TestEntity("next transaction"));
        await db.UnitOfWork.Commit();

        List<TestEntity> saved = await db.Context.Set<TestEntity>().ToListAsync();
        Assert.Equal("next transaction", Assert.Single(saved).Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_DoesNotReplayAbandonedCallbacks(bool synchronous)
    {
        await using var db = new DatabaseFixture();
        await db.UnitOfWork.CreateTransaction();
        int callbacks = 0;
        await db.UnitOfWork.RegisterPostCommitAction(() => { callbacks++; return Task.CompletedTask; });

        if (synchronous) db.UnitOfWork.Dispose();
        else await db.UnitOfWork.DisposeAsync();

        await db.UnitOfWork.CreateTransaction();
        await db.UnitOfWork.Commit();

        Assert.Equal(0, callbacks);
    }

    [Fact]
    public async Task Commit_CallbackFailureDoesNotUndoCommitOrSkipOtherCallbacks()
    {
        await using var db = new DatabaseFixture();
        await db.UnitOfWork.CreateTransaction();
        await db.Repository.Add(default, new TestEntity());
        await db.UnitOfWork.RegisterPostCommitAction(() => throw new InvalidOperationException("callback failure"));
        int callbacks = 0;
        await db.UnitOfWork.RegisterPostCommitAction(() => { callbacks++; return Task.CompletedTask; });

        await db.UnitOfWork.Commit();

        Assert.Equal(1, callbacks);
        Assert.Equal(1, await db.Repository.Count());
    }

    [Fact]
    public async Task TransactionLifecycle_RejectsInvalidTransitions()
    {
        await using var db = new DatabaseFixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.UnitOfWork.Commit());
        await db.UnitOfWork.CreateTransaction();
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.UnitOfWork.CreateTransaction());
    }
}
