using Microsoft.Extensions.DependencyInjection;

namespace Consumer.Application;

public static partial class ConfigureHandlerServices
{
    private static partial void ConfigureGenerated(IServiceCollection services);

    public static IServiceCollection ConfigureApplication(this IServiceCollection services)
    {
        ConfigureGenerated(services);
        return services;
    }
}
