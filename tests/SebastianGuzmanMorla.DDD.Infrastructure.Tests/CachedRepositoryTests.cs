using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using SebastianGuzmanMorla.DDD.Infrastructure.Repositories;
using StackExchange.Redis;

namespace SebastianGuzmanMorla.DDD.Infrastructure.Tests;

public class CachedRepositoryTests
{
    private class CachedTestRepository(IServiceProvider services) : CachedRepository<TestDbContext, TestEntity>(services)
    {
        protected override string CacheKeyPrefix => "tests";
        protected override JsonTypeInfo<TestEntity> JsonTypeInfo =>
            TestJsonSerializerContext.Default.TestEntity;
    }

    private sealed class SharedCachedTestRepository(IServiceProvider services) : CachedTestRepository(services)
    {
        protected override bool CacheHasUniformVisibility => true;
    }

    private sealed class CacheFixture : IAsyncDisposable
    {
        public IDatabase Cache { get; } = Substitute.For<IDatabase>();
        public DatabaseFixture Db { get; }
        public CachedTestRepository Repository { get; }

        public CacheFixture(bool enableSharedCache = true)
        {
            IConnectionMultiplexer redis = Substitute.For<IConnectionMultiplexer>();
            redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(Cache);
            Db = new DatabaseFixture(services => services.AddSingleton(redis));
            Repository = enableSharedCache
                ? new SharedCachedTestRepository(Db.Services)
                : new CachedTestRepository(Db.Services);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    [Fact]
    public async Task DefaultReadsHonorDatabaseVisibilityWithAPopulatedSharedCache()
    {
        await using var fixture = new CacheFixture(enableSharedCache: false);
        var entity = new TestEntity("visible to first scope");
        await fixture.Db.Repository.Add(default, entity);
        fixture.Cache.StringGetAsync((RedisKey)$"tests:{entity.Id}", Arg.Any<CommandFlags>())
            .Returns((RedisValue)JsonSerializer.Serialize(entity, TestJsonSerializerContext.Default.TestEntity));
        fixture.Cache.KeyExistsAsync((RedisKey)$"tests:{entity.Id}", Arg.Any<CommandFlags>()).Returns(true);
        fixture.Cache.ClearReceivedCalls();

        fixture.Db.Context.VisibleName = entity.Name;
        Assert.True(await fixture.Repository.Any(entity.Id));
        Assert.Equal(entity.Id, (await fixture.Repository.FirstOrDefault(entity.Id))?.Id);

        fixture.Db.Context.VisibleName = "a different scope";
        Assert.False(await fixture.Repository.Any(entity.Id));
        Assert.Null(await fixture.Repository.FirstOrDefault(entity.Id));
        await fixture.Repository.Add(default, new TestEntity());
        Assert.Empty(fixture.Cache.ReceivedCalls());
    }

    [Fact]
    public async Task SharedCacheDoesNotReturnOrPopulateSoftDeletedEntities()
    {
        await using var fixture = new CacheFixture();
        var entity = new TestEntity { DeletedAt = DateTime.UtcNow };
        RedisKey key = $"tests:{entity.Id}";
        fixture.Cache.StringGetAsync(key, Arg.Any<CommandFlags>())
            .Returns((RedisValue)JsonSerializer.Serialize(entity, TestJsonSerializerContext.Default.TestEntity));

        await fixture.Repository.Add(default, entity);

        Assert.False(await fixture.Repository.Any(entity.Id));
        Assert.Null(await fixture.Repository.FirstOrDefault(entity.Id));
        await fixture.Cache.Received(1).KeyDeleteAsync(key);
        Assert.DoesNotContain(fixture.Cache.ReceivedCalls(), call => call.GetMethodInfo().Name == "StringSetAsync");
    }

    [Fact]
    public async Task SharedCacheAddOnlyPopulatesRowsVisibleToTheBackingQuery()
    {
        await using var fixture = new CacheFixture();
        fixture.Db.Context.VisibleName = "published fixture";
        var visible = new TestEntity("published fixture");
        var excluded = new TestEntity("excluded fixture");

        await fixture.Repository.Add(default, visible, excluded);

        ICall write = Assert.Single(fixture.Cache.ReceivedCalls(), call => call.GetMethodInfo().Name == "StringSetAsync");
        Assert.Equal($"tests:{visible.Id}", write.GetArguments()[0]!.ToString());
        Assert.False(await fixture.Repository.Any(excluded.Id));
        Assert.Null(await fixture.Repository.FirstOrDefault(excluded.Id));
    }

    [Fact]
    public async Task FirstOrDefault_TransactionReadDoesNotPublishUncommittedData()
    {
        await using var fixture = new CacheFixture();
        var entity = new TestEntity();
        await fixture.Db.UnitOfWork.CreateTransaction();
        await fixture.Db.Repository.Add(default, entity);
        // Flush inside the transaction so a database query can see the row.
        await fixture.Db.Context.SaveChangesAsync();

        Assert.NotNull(await fixture.Repository.FirstOrDefault(entity.Id));
        await fixture.Db.UnitOfWork.Rollback();

        Assert.Null(await fixture.Db.Repository.FirstOrDefault(entity.Id));
        Assert.DoesNotContain(fixture.Cache.ReceivedCalls(), i => i.GetMethodInfo().Name == "StringSetAsync");
    }

    [Fact]
    public async Task FirstOrDefault_TransactionReadsDatabaseInsteadOfStaleCache()
    {
        await using var fixture = new CacheFixture();
        var entity = new TestEntity();
        await fixture.Db.Repository.Add(default, entity);
        fixture.Cache.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)JsonSerializer.Serialize(entity, TestJsonSerializerContext.Default.TestEntity));
        await fixture.Db.UnitOfWork.CreateTransaction();
        entity.Rename("updated");
        await fixture.Repository.Update(default, entity);
        await fixture.Db.Context.SaveChangesAsync();

        TestEntity? actual = await fixture.Repository.FirstOrDefault(entity.Id);

        Assert.Equal("updated", actual?.Name);
        Assert.DoesNotContain(fixture.Cache.ReceivedCalls(), i => i.GetMethodInfo().Name == "StringGetAsync");
    }

    [Fact]
    public async Task Any_TransactionDoesNotReturnCachedRowThatWasDeleted()
    {
        await using var fixture = new CacheFixture();
        var entity = new TestEntity();
        await fixture.Db.Repository.Add(default, entity);
        fixture.Cache.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(true);
        await fixture.Db.UnitOfWork.CreateTransaction();
        await fixture.Repository.HardDelete(default, entity);

        Assert.False(await fixture.Repository.Any(entity.Id));
        Assert.DoesNotContain(fixture.Cache.ReceivedCalls(), i => i.GetMethodInfo().Name == "StringGetAsync");
    }

    [Fact]
    public async Task FirstOrDefault_CacheHitAvoidsDatabaseAndMissPopulatesCache()
    {
        await using var fixture = new CacheFixture();
        var cached = new TestEntity("cached");
        fixture.Cache.StringGetAsync((RedisKey)$"tests:{cached.Id}", Arg.Any<CommandFlags>())
            .Returns((RedisValue)JsonSerializer.Serialize(cached, TestJsonSerializerContext.Default.TestEntity));

        Assert.Equal("cached", (await fixture.Repository.FirstOrDefault(cached.Id))?.Name);
        Assert.Equal(0, await fixture.Db.Repository.Count());

        var stored = new TestEntity("stored");
        await fixture.Db.Repository.Add(default, stored);
        Assert.Equal("stored", (await fixture.Repository.FirstOrDefault(stored.Id))?.Name);
        ICall write = Assert.Single(fixture.Cache.ReceivedCalls(), i => i.GetMethodInfo().Name == "StringSetAsync");
        Assert.Equal($"tests:{stored.Id}", write.GetArguments()[0]!.ToString());
    }

    [Fact]
    public async Task Add_OnlyPopulatesCacheAfterCommit()
    {
        await using var fixture = new CacheFixture();
        await fixture.Db.UnitOfWork.CreateTransaction();
        var entity = new TestEntity();
        await fixture.Repository.Add(default, entity);
        Assert.DoesNotContain(fixture.Cache.ReceivedCalls(), i => i.GetMethodInfo().Name == "StringSetAsync");

        await fixture.Db.UnitOfWork.Commit();

        ICall write = Assert.Single(fixture.Cache.ReceivedCalls(), i => i.GetMethodInfo().Name == "StringSetAsync");
        Assert.Equal($"tests:{entity.Id}", write.GetArguments()[0]!.ToString());
    }

    [Fact]
    public async Task Add_LazyEnumerableCachesTheSameEntityThatWasSaved()
    {
        await using var fixture = new CacheFixture();
        IEnumerable<TestEntity> items = Enumerable.Range(0, 1).Select(_ => new TestEntity());

        await fixture.Repository.Add(default, items);

        ICall write = Assert.Single(fixture.Cache.ReceivedCalls(), i => i.GetMethodInfo().Name == "StringSetAsync");
        TestEntity? cached = JsonSerializer.Deserialize(write.GetArguments()[1]!.ToString()!, TestJsonSerializerContext.Default.TestEntity);
        Assert.NotNull(cached);
        Assert.NotNull(await fixture.Db.Repository.FirstOrDefault(cached.Id));
    }

    [Theory]
    [InlineData("Update")]
    [InlineData("SoftDelete")]
    [InlineData("HardDelete")]
    [InlineData("Upsert")]
    public async Task Mutation_LazyEnumerableInvalidatesTheSavedEntityAfterCommit(string operation)
    {
        await using var fixture = new CacheFixture();
        var entity = new TestEntity();
        await fixture.Db.Repository.Add(default, entity);
        await fixture.Db.UnitOfWork.CreateTransaction();
        int enumerations = 0;
        IEnumerable<TestEntity> Items()
        {
            enumerations++;
            yield return enumerations == 1 ? entity : new TestEntity();
        }

        switch (operation)
        {
            case "Update": await fixture.Repository.Update(default, Items()); break;
            case "Upsert": await fixture.Repository.Upsert(default, Items()); break;
            case "SoftDelete": await fixture.Repository.SoftDelete(default, Items()); break;
            case "HardDelete": await fixture.Repository.HardDelete(default, Items()); break;
        }
        Assert.DoesNotContain(fixture.Cache.ReceivedCalls(), i => i.GetMethodInfo().Name == "KeyDeleteAsync");
        await fixture.Db.UnitOfWork.Commit();

        ICall deletion = Assert.Single(fixture.Cache.ReceivedCalls(), i => i.GetMethodInfo().Name == "KeyDeleteAsync");
        RedisKey[] keys = Assert.IsType<RedisKey[]>(deletion.GetArguments()[0]);
        Assert.Equal($"tests:{entity.Id}", Assert.Single(keys).ToString());
        Assert.Equal(1, enumerations);
    }
}
