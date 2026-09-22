using Consumer.Domain.Entities.Customers;
using Consumer.Domain.Interfaces.Repositories;
using Consumer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Consumer.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class CacheRecoveryTests(ConsumerContainers fixture) : IClassFixture<ConsumerContainers>
{
    [Fact]
    public async Task ExpiredEntry_IsReloadedFromPostgresWithFreshExpiry()
    {
        var customer = new Customer($"Expiry-{Guid.NewGuid():N}");
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ICustomerRepository repository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        await repository.Add(default, customer);
        var key = CachedCustomerRepository.Key(customer.Id);
        Assert.True(await fixture.Cache.KeyExistsAsync(key));
        await fixture.Cache.KeyExpireAsync(key, TimeSpan.FromMilliseconds(1));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (await fixture.Cache.KeyExistsAsync(key)) await Task.Delay(10, timeout.Token);

        Customer? loaded = await repository.FirstOrDefault(customer.Id);

        Assert.Equal(customer.Name, loaded!.Name);
        Assert.InRange((await fixture.Cache.KeyTimeToLiveAsync(key))!.Value, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task UpsertAndHardDelete_UsePostgresAndInvalidateCachedEntity()
    {
        var customer = new Customer($"Upsert-{Guid.NewGuid():N}");
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ICustomerRepository repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        IUnitOfWork<DatabaseContext> uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork<DatabaseContext>>();
        await repo.Add(default, customer);
        customer.Rename(customer.Name + "-updated");
        await uow.CreateTransaction();
        Assert.Equal(1, await repo.Upsert(default, customer));
        await uow.Commit();
        Assert.False(await fixture.Cache.KeyExistsAsync(CachedCustomerRepository.Key(customer.Id)));
        Assert.Equal(customer.Name, (await repo.FirstOrDefault(customer.Id))!.Name);
        await uow.CreateTransaction();
        Assert.Equal(1, await repo.HardDelete(default, customer));
        await uow.Commit();
        Assert.False(await fixture.Cache.KeyExistsAsync(CachedCustomerRepository.Key(customer.Id)));
        Assert.False(await scope.ServiceProvider.GetRequiredService<DatabaseContext>().Set<Customer>().AnyAsync(x => x.Id == customer.Id));
    }

    [Fact]
    public async Task ConcurrentCreates_PostgresUniqueConstraintAllowsExactlyOneCommit()
    {
        var name = $"Concurrent-{Guid.NewGuid():N}";
        async Task<bool> Insert()
        {
            await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
            ICustomerRepository repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
            IUnitOfWork<DatabaseContext> uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork<DatabaseContext>>();
            await uow.CreateTransaction();
            await repo.Add(default, new Customer(name));
            try { await uow.Commit(); return true; }
            catch (DbUpdateException error) when (error.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
            { await uow.Rollback(); return false; }
        }
        var results = await Task.WhenAll(Insert(), Insert());
        Assert.Single(results, committed => committed);
        await using AsyncServiceScope verification = fixture.Services.CreateAsyncScope();
        Assert.Equal(1, await verification.ServiceProvider.GetRequiredService<DatabaseContext>().Set<Customer>().CountAsync(x => x.Name == name));
    }

    [Fact]
    public async Task RedisOutageAfterTransactionStarts_DoesNotUndoCommittedPostgresWrite()
    {
        // A separate cache container keeps the outage isolated from the shared fixture.
        await using RedisContainer redis = new RedisBuilder("redis:7.4.5-alpine").Build();
        await redis.StartAsync();
        var configuration = ConfigurationOptions.Parse(redis.GetConnectionString());
        configuration.AsyncTimeout = 1000;
        configuration.AbortOnConnectFail = false;
        await using ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(configuration);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IUnitOfWork<DatabaseContext> uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork<DatabaseContext>>();
        await using ServiceProvider cacheServices = new ServiceCollection().AddSingleton(uow)
            .AddSingleton<IConnectionMultiplexer>(connection).BuildServiceProvider();
        var repo = new CachedCustomerRepository(cacheServices);
        var customer = new Customer($"Outage-{Guid.NewGuid():N}");
        await uow.CreateTransaction();
        await repo.Add(default, customer);
        await redis.StopAsync();

        await uow.Commit();

        Assert.False(uow.TransactionEnabled);
        await using AsyncServiceScope verification = fixture.Services.CreateAsyncScope();
        Assert.True(await verification.ServiceProvider.GetRequiredService<DatabaseContext>().Set<Customer>().AnyAsync(x => x.Id == customer.Id));
    }
}
