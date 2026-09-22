using Consumer.Application;
using Consumer.Contracts.Interfaces.Localization;
using Consumer.Domain;
using Consumer.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;

namespace SebastianGuzmanMorla.DDD.Testing;

internal sealed class ConsumerFixture : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    public ServiceProvider Services { get; }

    public ConsumerFixture(IRuleLocalization ruleLocalization, IGeneralLocalization generalLocalization)
    {
        _connection.Open();
        Services = new ServiceCollection()
            .AddSingleton(ruleLocalization)
            .AddSingleton(generalLocalization)
            .ConfigureDomain()
            .ConfigureInfrastructure(options => options.UseSqlite(_connection))
            .ConfigureApplication()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using IServiceScope scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<DatabaseContext>().Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
