using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.DDD.Testing;

namespace SebastianGuzmanMorla.DDD.Generator.Tests;

public sealed class RegistrationShapeTests
{
    [Theory]
    [InlineData("namespace App;", "")]
    [InlineData("namespace Outer { namespace App {", "}}")]
    [InlineData("", "")]
    public void HandlerRegistration_SupportsNamespacesNestedHandlersAndOpenGenericNotifications(string prefix, string suffix)
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SebastianGuzmanMorla.DDD.Domain.Interfaces;
            using SebastianGuzmanMorla.DDD.Domain.Messaging;
            """ + prefix + """
            public static partial class ConfigureHandlerServices
            {
                private static partial void ConfigureGenerated(IServiceCollection services);
                public static IServiceCollection Build() { var services = new ServiceCollection(); ConfigureGenerated(services); return services; }
            }
            public abstract class TestRequest : Request<Response> { }
            public class Container
            {
                public partial class Handler : IRequestHandler<TestRequest, Response>
                {
                    public Task<Response> Handle(TestRequest request, CancellationToken token = default) => Task.FromResult(new Response());
                }
                public partial class Handler : IRequestHandler<TestRequest, Response> { }
            }
            public sealed class Event : INotification
            {
                public DateTime Timestamp => DateTime.UtcNow;
                public Task Handle(IServiceProvider services, CancellationToken token = default) => Task.CompletedTask;
            }
            public sealed class NotificationHandler<T> : INotificationHandler<T> where T : INotification
            {
                public Task Handle(T notification, CancellationToken token = default) => Task.CompletedTask;
            }
            """ + suffix;
        GeneratorCompilation.Verify(source, new ConfigureHandlerServicesGenerator(), assembly =>
        {
            Type marker = assembly.GetTypes().Single(t => t.Name == "ConfigureHandlerServices");
            var services = (IServiceCollection)marker.GetMethod("Build")!.Invoke(null, null)!;
            ServiceDescriptor handlerDescriptor = Assert.Single(services, d => d.ImplementationType?.Name == "Handler");
            Assert.Equal(ServiceLifetime.Scoped, handlerDescriptor.Lifetime);
            using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            using IServiceScope first = provider.CreateScope();
            using IServiceScope second = provider.CreateScope();
            var instance = first.ServiceProvider.GetRequiredService(handlerDescriptor.ServiceType);
            Assert.Same(instance, first.ServiceProvider.GetRequiredService(handlerDescriptor.ServiceType));
            Assert.NotSame(instance, second.ServiceProvider.GetRequiredService(handlerDescriptor.ServiceType));
            Type notificationType = typeof(INotificationHandler<>).MakeGenericType(assembly.GetTypes().Single(t => t.Name == "Event"));
            Assert.Same(first.ServiceProvider.GetRequiredService(notificationType), second.ServiceProvider.GetRequiredService(notificationType));
        });
    }

    [Theory]
    [InlineData("namespace App;", "")]
    [InlineData("namespace Outer { namespace App {", "}}")]
    [InlineData("", "")]
    public void RepositoryRegistration_RegistersEachInterfaceOnceForSplitPartialTypes(string prefix, string suffix)
    {
        var source = """
            using System;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;
            using SebastianGuzmanMorla.DDD.Domain.Entities;
            using SebastianGuzmanMorla.DDD.Domain.Interfaces;
            using SebastianGuzmanMorla.DDD.Infrastructure.Repositories;
            """ + prefix + """
            public static partial class ConfigureRepositoryServices
            {
                private static partial void ConfigureGenerated(IServiceCollection services);
                public static IServiceCollection Build() { var services = new ServiceCollection(); ConfigureGenerated(services); return services; }
            }
            public class TestEntity : Entity { }
            public class TestContext : DbContext { }
            public interface ICustomRepository : IRepository<TestEntity> { }
            public partial class CustomerRepository(IServiceProvider services) : Repository<TestContext, TestEntity>(services), ICustomRepository { }
            public partial class CustomerRepository : ICustomRepository { }
            """ + suffix;
        GeneratorCompilation.Verify(source, new ConfigureRepositoryServicesGenerator(), assembly =>
        {
            Type marker = assembly.GetTypes().Single(t => t.Name == "ConfigureRepositoryServices");
            var services = (IServiceCollection)marker.GetMethod("Build")!.Invoke(null, null)!;
            Assert.Equal(2, services.Count);
            Assert.Equal(2, services.Select(d => d.ServiceType).Distinct().Count());
            Assert.All(services, d => Assert.Equal(ServiceLifetime.Scoped, d.Lifetime));
        });
    }
}
