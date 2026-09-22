using Microsoft.Data.Sqlite;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Entities;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.DDD.Infrastructure.Repositories;

namespace SebastianGuzmanMorla.DDD.Infrastructure.Tests;

public sealed class TestEntity : Entity
{
    public string Name { get; private set; }

    [JsonConstructor]
    public TestEntity(string name = "original") => Name = name;

    public void Rename(string name) => Name = name;
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestEntity>();
    }
}

public sealed class TestRepository(IServiceProvider services) : Repository<TestDbContext, TestEntity>(services);

internal sealed class DatabaseFixture : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    public TestDbContext Context { get; }
    public UnitOfWork<TestDbContext> UnitOfWork { get; }
    public ServiceProvider Services { get; }
    public TestRepository Repository { get; }

    public DatabaseFixture(Action<IServiceCollection>? configure = null)
    {
        _connection.Open();
        Context = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options);
        Context.Database.EnsureCreated();
        UnitOfWork = new UnitOfWork<TestDbContext>(Context);
        IServiceCollection services = new ServiceCollection().AddSingleton<IUnitOfWork<TestDbContext>>(UnitOfWork);
        configure?.Invoke(services);
        Services = services.BuildServiceProvider();
        Repository = new TestRepository(Services);
    }

    public async ValueTask DisposeAsync()
    {
        await UnitOfWork.DisposeAsync();
        await Services.DisposeAsync();
        await Context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
