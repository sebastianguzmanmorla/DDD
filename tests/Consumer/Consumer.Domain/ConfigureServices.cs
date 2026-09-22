using Microsoft.Extensions.DependencyInjection;

namespace Consumer.Domain;

public static partial class ConfigureServices
{
    private static partial void RegisterValidators(IServiceCollection services);

    public static IServiceCollection ConfigureDomain(this IServiceCollection services)
    {
        RegisterValidators(services);
        return services;
    }
}
