using System.Text.Json.Serialization.Metadata;
using Consumer.Application;
using Consumer.Domain;
using Consumer.Domain.Entities.Customers;
using Consumer.Domain.Interfaces.Repositories;
using Consumer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SebastianGuzmanMorla.DDD.Infrastructure.Repositories;
using SebastianGuzmanMorla.DDD.Testing;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Consumer.Integration.Tests;

// One pair of isolated containers per test class; no fixed host ports or external databases.
public sealed class ConsumerContainers : ValidatorTestBase, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.6-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7.4.5-alpine").Build();
    private ConnectionMultiplexer? _connection;
    public ServiceProvider Services { get; private set; } = null!;
    public IDatabase Cache => _connection!.GetDatabase();

    public async Task InitializeAsync()
    {
        try
        {
            await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
            _connection = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
            IServiceCollection services = new ServiceCollection()
                .AddSingleton(RuleLocalization)
                .AddSingleton(GeneralLocalization)
                .AddSingleton<IConnectionMultiplexer>(_connection)
                .ConfigureDomain()
                .ConfigureInfrastructure(options => options.UseNpgsql(_postgres.GetConnectionString()))
                .ConfigureApplication();
            services.Replace(ServiceDescriptor.Scoped<ICustomerRepository, CachedCustomerRepository>());
            Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            await using AsyncServiceScope scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DatabaseContext>().Database.EnsureCreatedAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (Services is not null) await Services.DisposeAsync();
            if (_connection is not null) await _connection.DisposeAsync();
        }
        finally
        {
            await Task.WhenAll(_redis.DisposeAsync().AsTask(), _postgres.DisposeAsync().AsTask());
        }
    }
}

// Test adapter using the consumer's real entity, JSON context, interface, and database mapping.
internal sealed class CachedCustomerRepository(IServiceProvider services)
    : CachedRepository<DatabaseContext, Customer>(services), ICustomerRepository
{
    public static string Key(Guid id) => $"consumer:customers:{id}";
    protected override string CacheKeyPrefix => "consumer:customers";
    protected override JsonTypeInfo<Customer> JsonTypeInfo => DomainJsonSerializerContext.Default.Customer;
    public Task<bool> NameExists(string name, CancellationToken cancellationToken = default) =>
        Queryable.AnyAsync(x => x.Name == name, cancellationToken);
}
