using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.DDD.Infrastructure.Repositories;

namespace Consumer.Infrastructure;

public static partial class ConfigureRepositoryServices
{
    private static partial void ConfigureGenerated(IServiceCollection services);

    public static IServiceCollection ConfigureInfrastructure(this IServiceCollection services, Action<DbContextOptionsBuilder> options)
    {
        services.AddDbContext<DatabaseContext>(options);
        services.AddScoped<IUnitOfWork<DatabaseContext>, UnitOfWork<DatabaseContext>>();
        ConfigureGenerated(services);
        return services;
    }
}
