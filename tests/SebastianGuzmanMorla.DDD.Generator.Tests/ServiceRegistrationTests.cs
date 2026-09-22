using System.Net;
using Consumer.Application.Handlers.Customers;
using Consumer.Contracts.Messaging.Customers;
using Consumer.Domain.Entities.Customers;
using Consumer.Domain.Interfaces.Repositories;
using Consumer.Infrastructure;
using Consumer.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.DDD.Domain.Messaging;
using SebastianGuzmanMorla.DDD.Extensions;
using SebastianGuzmanMorla.DDD.Testing;
using SebastianGuzmanMorla.Validator.Interfaces;

namespace SebastianGuzmanMorla.DDD.Generator.Tests;

public class ServiceRegistrationTests : ValidatorTestBase
{
    [Fact]
    public async Task GeneratedRegistrations_ResolveRealHandlersRepositoriesAndDomainValidator()
    {
        await using var fixture = new ConsumerFixture(RuleLocalization, GeneralLocalization);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;

        Assert.IsType<CustomerRepository>(sp.GetRequiredService<ICustomerRepository>());
        Assert.IsType<CreateCustomerRequestHandler>(sp.GetRequiredService<IRequestHandler<CreateCustomerRequest, CreateCustomerResponse>>());
        Assert.IsType<Consumer.Domain.Validators.Customers.CreateCustomerRequestValidator>(sp.GetRequiredService<IValidator<CreateCustomerRequest>>());

        IEntityType model = sp.GetRequiredService<DatabaseContext>().Model.FindEntityType(typeof(Customer))!;
        Assert.Equal("Customers", model.GetTableName());
        Assert.Equal(100, model.FindProperty(nameof(Customer.Name))!.GetMaxLength());
        Assert.Contains(model.GetIndexes(), index => index.IsUnique);
    }
}
