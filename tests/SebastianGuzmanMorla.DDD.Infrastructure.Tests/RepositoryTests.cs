using Microsoft.EntityFrameworkCore;

namespace SebastianGuzmanMorla.DDD.Infrastructure.Tests;

public class RepositoryTests
{
    [Fact]
    public async Task AddAndUpdate_PersistWithoutExplicitTransactionAndDetachEntities()
    {
        await using var db = new DatabaseFixture();
        var entity = new TestEntity { UpdatedAt = DateTime.UnixEpoch };

        await db.Repository.Add(default, entity);
        Assert.True(entity.UpdatedAt > DateTime.UnixEpoch);
        Assert.Empty(db.Context.ChangeTracker.Entries());

        entity.Rename("updated");
        await db.Repository.Update(default, entity);

        Assert.Equal("updated", (await db.Repository.FirstOrDefault(entity.Id))?.Name);
        Assert.Empty(db.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task SoftDelete_HidesEntityFromAllReadsButKeepsDatabaseRow()
    {
        await using var db = new DatabaseFixture();
        var entity = new TestEntity();
        await db.Repository.Add(default, entity);

        await db.Repository.SoftDelete(default, entity);

        Assert.False(await db.Repository.Any(entity.Id));
        Assert.Null(await db.Repository.FirstOrDefault(entity.Id));
        Assert.Equal(0, await db.Repository.Count());
        Assert.NotNull((await db.Context.Set<TestEntity>().SingleAsync()).DeletedAt);
    }

    [Fact]
    public async Task HardDelete_RemovesOnlyRequestedRowsIncludingSoftDeletedRows()
    {
        await using var db = new DatabaseFixture();
        var deleted = new TestEntity { DeletedAt = DateTime.UtcNow };
        var kept = new TestEntity();
        await db.Repository.Add(default, deleted, kept);

        Assert.Equal(1, await db.Repository.HardDelete(default, deleted));
        Assert.Equal(kept.Id, (await db.Context.Set<TestEntity>().SingleAsync()).Id);
    }
}
