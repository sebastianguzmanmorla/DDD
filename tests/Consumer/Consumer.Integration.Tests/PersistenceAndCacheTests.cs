using System.Net;
using System.Text.Json;
using Consumer.Contracts.Messaging.Customers;
using Consumer.Domain;
using Consumer.Domain.Entities.Customers;
using Consumer.Domain.Interfaces.Repositories;
using Consumer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SebastianGuzmanMorla.DDD.Extensions;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace Consumer.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class PersistenceAndCacheTests(ConsumerContainers fixture) : IClassFixture<ConsumerContainers>
{
    private static string UniqueName() => $"Customer-{Guid.NewGuid():N}";

    [Fact]
    public async Task Handlers_PersistRenameAndSoftDeleteAcrossScopesAndInvalidateRedis()
    {
        Guid id;
        var name = UniqueName();
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            CreateCustomerResponse created = await new CreateCustomerRequest { Name = name }
                .Handle<CreateCustomerRequest, CreateCustomerResponse>(scope.ServiceProvider);
            Assert.Equal(HttpStatusCode.OK, created.Status);
            id = created.Id;
        }
        Assert.True(await fixture.Cache.KeyExistsAsync(CachedCustomerRepository.Key(id)));
        TimeSpan? expiry = await fixture.Cache.KeyTimeToLiveAsync(CachedCustomerRepository.Key(id));
        Assert.InRange(expiry!.Value, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10));

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            Response renamed = await new RenameCustomerRequest { Id = id, Name = name + "-renamed" }
                .Handle<RenameCustomerRequest, Response>(scope.ServiceProvider);
            Assert.Equal(HttpStatusCode.OK, renamed.Status);
        }
        Assert.False(await fixture.Cache.KeyExistsAsync(CachedCustomerRepository.Key(id)));
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            Customer? customer = await scope.ServiceProvider.GetRequiredService<ICustomerRepository>().FirstOrDefault(id);
            Assert.Equal(name + "-renamed", customer!.Name);
            Assert.Equal(DateTimeKind.Utc, customer.CreatedAt.Kind);
            Assert.Equal(DateTimeKind.Utc, customer.UpdatedAt.Kind);
            Assert.True(await fixture.Cache.KeyExistsAsync(CachedCustomerRepository.Key(id)));
            Response deleted = await new DeleteCustomerRequest { Id = id }.Handle<DeleteCustomerRequest, Response>(scope.ServiceProvider);
            Assert.Equal(HttpStatusCode.OK, deleted.Status);
        }
        Assert.False(await fixture.Cache.KeyExistsAsync(CachedCustomerRepository.Key(id)));
        await using AsyncServiceScope finalScope = fixture.Services.CreateAsyncScope();
        Assert.Null(await finalScope.ServiceProvider.GetRequiredService<ICustomerRepository>().FirstOrDefault(id));
        Customer stored = await finalScope.ServiceProvider.GetRequiredService<DatabaseContext>().Set<Customer>().SingleAsync(x => x.Id == id);
        Assert.Equal(DateTimeKind.Utc, stored.DeletedAt!.Value.Kind);
    }

    [Fact]
    public async Task RedisRoundTrip_PreservesEntityIdentityAndPrivateDomainState()
    {
        var entity = new Customer(UniqueName());
        var key = CachedCustomerRepository.Key(entity.Id);
        await fixture.Cache.StringSetAsync(key, JsonSerializer.Serialize(entity, DomainJsonSerializerContext.Default.Customer));
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ICustomerRepository repository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();

        Customer? cached = await repository.FirstOrDefault(entity.Id);

        Assert.NotNull(cached);
        Assert.Equal(entity.Id, cached.Id);
        Assert.Equal(entity.Name, cached.Name);
        Assert.True(await repository.Any(entity.Id));
        Assert.False(await scope.ServiceProvider.GetRequiredService<DatabaseContext>().Set<Customer>().AnyAsync(x => x.Id == entity.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UncommittedInsert_IsInvisibleToOtherScopeAndRedis_AndIsDiscarded(bool dispose)
    {
        var entity = new Customer(UniqueName());
        await using (AsyncServiceScope writer = fixture.Services.CreateAsyncScope())
        {
            IUnitOfWork<DatabaseContext> uow = writer.ServiceProvider.GetRequiredService<IUnitOfWork<DatabaseContext>>();
            ICustomerRepository repository = writer.ServiceProvider.GetRequiredService<ICustomerRepository>();
            await uow.CreateTransaction();
            await repository.Add(default, entity);
            // Flush SQL without committing to exercise PostgreSQL transaction isolation.
            await uow.Context.SaveChangesAsync();
            Assert.NotNull(await repository.FirstOrDefault(entity.Id));
            Assert.False(await fixture.Cache.KeyExistsAsync(CachedCustomerRepository.Key(entity.Id)));
            await using AsyncServiceScope observer = fixture.Services.CreateAsyncScope();
            Assert.False(await observer.ServiceProvider.GetRequiredService<ICustomerRepository>().Any(entity.Id));
            if (!dispose) await uow.Rollback();
        }
        await using AsyncServiceScope verification = fixture.Services.CreateAsyncScope();
        Assert.False(await verification.ServiceProvider.GetRequiredService<ICustomerRepository>().Any(entity.Id));
        Assert.False(await fixture.Cache.KeyExistsAsync(CachedCustomerRepository.Key(entity.Id)));
    }

    [Fact]
    public async Task Transaction_BypassesStaleRedisAndRollbackPreservesCommittedCache()
    {
        var entity = new Customer(UniqueName());
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ICustomerRepository repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        IUnitOfWork<DatabaseContext> uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork<DatabaseContext>>();
        await repo.Add(default, entity);
        var original = entity.Name;
        await uow.CreateTransaction();
        entity.Rename(original + "-uncommitted");
        await repo.Update(default, entity);
        await uow.Context.SaveChangesAsync();
        Assert.Equal(entity.Name, (await repo.FirstOrDefault(entity.Id))!.Name);
        await using (AsyncServiceScope observer = fixture.Services.CreateAsyncScope())
        {
            Assert.Equal(original, (await observer.ServiceProvider.GetRequiredService<ICustomerRepository>().FirstOrDefault(entity.Id))!.Name);
        }
        await uow.Rollback();
        Assert.Equal(original, (await repo.FirstOrDefault(entity.Id))!.Name);
        Assert.Equal(original, (await scope.ServiceProvider.GetRequiredService<DatabaseContext>().Set<Customer>().SingleAsync(x => x.Id == entity.Id)).Name);
    }

    [Fact]
    public async Task UniqueConstraintFailure_RollsBackAndDoesNotPublishCacheOrPoisonNextTransaction()
    {
        var name = UniqueName();
        var first = new Customer(name);
        var duplicate = new Customer(name);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ICustomerRepository repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        IUnitOfWork<DatabaseContext> uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork<DatabaseContext>>();
        await repo.Add(default, first);
        await uow.CreateTransaction();

        await repo.Add(default, duplicate);
        DbUpdateException error = await Assert.ThrowsAsync<DbUpdateException>(() => uow.Commit());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        await uow.Rollback();
        Assert.False(await fixture.Cache.KeyExistsAsync(CachedCustomerRepository.Key(duplicate.Id)));
        Assert.Empty(uow.Context.ChangeTracker.Entries());
        var next = new Customer(UniqueName());
        await uow.CreateTransaction();
        await repo.Add(default, next);
        await uow.Commit();
        Assert.True(await fixture.Cache.KeyExistsAsync(CachedCustomerRepository.Key(next.Id)));
    }

    [Fact]
    public async Task DomainValidation_RejectsDuplicateBeforeOpeningTransaction()
    {
        var name = UniqueName();
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;
        Assert.Equal(HttpStatusCode.OK, (await new CreateCustomerRequest { Name = name }.Handle<CreateCustomerRequest, CreateCustomerResponse>(sp)).Status);
        CreateCustomerResponse duplicate = await new CreateCustomerRequest { Name = name }.Handle<CreateCustomerRequest, CreateCustomerResponse>(sp);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.Status);
        Assert.Equal("Customer already exists", Assert.Single(duplicate.Errors!["$.Name"]));
        Assert.False(sp.GetRequiredService<IUnitOfWork<DatabaseContext>>().TransactionEnabled);
    }

    [Fact]
    public async Task CancelledRequest_PropagatesCancellationWithoutPersistingCustomer()
    {
        var name = UniqueName();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CreateCustomerRequest { Name = name }
            .Handle<CreateCustomerRequest, CreateCustomerResponse>(scope.ServiceProvider, cancellation.Token));
        Assert.False(await scope.ServiceProvider.GetRequiredService<ICustomerRepository>().NameExists(name));
    }
}
