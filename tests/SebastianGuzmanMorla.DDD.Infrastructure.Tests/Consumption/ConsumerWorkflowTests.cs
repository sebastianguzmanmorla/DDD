using System.Net;
using Consumer.Contracts.Messaging.Customers;
using Consumer.Domain.Entities.Customers;
using Consumer.Domain.Interfaces.Repositories;
using Consumer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.DDD.Domain.Messaging;
using SebastianGuzmanMorla.DDD.Extensions;
using SebastianGuzmanMorla.DDD.Testing;

namespace SebastianGuzmanMorla.DDD.Infrastructure.Tests.Consumption;

public class ConsumerWorkflowTests : ValidatorTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnusedUnitOfWork_CanBeDisposedWithItsScope(bool synchronous)
    {
        await using var fixture = new ConsumerFixture(RuleLocalization, GeneralLocalization);
        if (synchronous)
        {
            using IServiceScope scope = fixture.Services.CreateScope();
            Assert.False(scope.ServiceProvider.GetRequiredService<IUnitOfWork<DatabaseContext>>().TransactionEnabled);
        }
        else
        {
            await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
            Assert.False(scope.ServiceProvider.GetRequiredService<IUnitOfWork<DatabaseContext>>().TransactionEnabled);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task GeneratedInterfaceValidation_RejectsInvalidPageSizeBeforeQuery(int size)
    {
        await using var fixture = new ConsumerFixture(RuleLocalization, GeneralLocalization);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();

        GetCustomersResponse response = await new GetCustomersRequest { Size = size }
            .Handle<GetCustomersRequest, GetCustomersResponse>(scope.ServiceProvider);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.NotEmpty(response.Errors!["$.Size"]);
        Assert.Null(response.Items);
    }

    [Fact]
    public async Task PageHandler_UsesProjectionAndExcludesSoftDeletedCustomers()
    {
        await using var fixture = new ConsumerFixture(RuleLocalization, GeneralLocalization);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;
        CreateCustomerResponse first = await new CreateCustomerRequest { Name = "Ada" }.Handle<CreateCustomerRequest, CreateCustomerResponse>(sp);
        CreateCustomerResponse second = await new CreateCustomerRequest { Name = "Grace" }.Handle<CreateCustomerRequest, CreateCustomerResponse>(sp);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Response deleted = await new DeleteCustomerRequest { Id = first.Id }.Handle<DeleteCustomerRequest, Response>(sp);
        Assert.Equal(HttpStatusCode.OK, deleted.Status);

        GetCustomersResponse page = await new GetCustomersRequest { Size = 1 }.Handle<GetCustomersRequest, GetCustomersResponse>(sp);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.Pages);
        Assert.Equal(1, page.Total);
        Assert.Equal("Grace", Assert.Single(page.Items!).Name);
    }

    [Fact]
    public async Task CreateRenameDelete_ThroughHandlersPersistsDomainBehaviorAndSoftDeletion()
    {
        await using var fixture = new ConsumerFixture(RuleLocalization, GeneralLocalization);
        Guid id;
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            IServiceProvider sp = scope.ServiceProvider;
            CreateCustomerResponse created = await new CreateCustomerRequest { Name = "Ada" }.Handle<CreateCustomerRequest, CreateCustomerResponse>(sp);
            Assert.Equal(HttpStatusCode.OK, created.Status);
            id = created.Id;
            Assert.NotEqual(Guid.Empty, id);

            Response renamed = await new RenameCustomerRequest { Id = id, Name = "Grace" }.Handle<RenameCustomerRequest, Response>(sp);
            Assert.Equal(HttpStatusCode.OK, renamed.Status);
        }

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            IServiceProvider sp = scope.ServiceProvider;
            Customer? customer = await sp.GetRequiredService<ICustomerRepository>().FirstOrDefault(id);
            Assert.NotNull(customer);
            Assert.Equal("Grace", customer.Name);
            Assert.Equal(DateTimeKind.Utc, customer.CreatedAt.Kind);
            Assert.Equal(DateTimeKind.Utc, customer.UpdatedAt.Kind);
            Response deleted = await new DeleteCustomerRequest { Id = id }.Handle<DeleteCustomerRequest, Response>(sp);
            Assert.Equal(HttpStatusCode.OK, deleted.Status);
        }

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            IServiceProvider sp = scope.ServiceProvider;
            Assert.Null(await sp.GetRequiredService<ICustomerRepository>().FirstOrDefault(id));
            // Storage inspection belongs to the test fixture; the handlers use repositories only.
            Customer stored = await sp.GetRequiredService<DatabaseContext>().Set<Customer>().SingleAsync();
            Assert.NotNull(stored.DeletedAt);
            Assert.Equal(DateTimeKind.Utc, stored.DeletedAt.Value.Kind);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task InvalidContract_Returns400WithoutPersistingCustomer(string? name)
    {
        await using var fixture = new ConsumerFixture(RuleLocalization, GeneralLocalization);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;

        CreateCustomerResponse response = await new CreateCustomerRequest { Name = name }.Handle<CreateCustomerRequest, CreateCustomerResponse>(sp);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.NotEmpty(response.Errors!["$.Name"]);
        Assert.Equal(0, await sp.GetRequiredService<ICustomerRepository>().Count());
        Assert.False(sp.GetRequiredService<IUnitOfWork<DatabaseContext>>().TransactionEnabled);
    }

    [Fact]
    public async Task DuplicateCustomer_IsRejectedByGeneratedDomainValidator()
    {
        await using var fixture = new ConsumerFixture(RuleLocalization, GeneralLocalization);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;
        Assert.Equal(HttpStatusCode.OK, (await new CreateCustomerRequest { Name = "Ada" }.Handle<CreateCustomerRequest, CreateCustomerResponse>(sp)).Status);

        CreateCustomerResponse duplicate = await new CreateCustomerRequest { Name = "Ada" }.Handle<CreateCustomerRequest, CreateCustomerResponse>(sp);

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.Status);
        Assert.Equal("Customer already exists", Assert.Single(duplicate.Errors!["$.Name"]));
        Assert.Equal(1, await sp.GetRequiredService<ICustomerRepository>().Count());
    }

    [Fact]
    public async Task DomainFailure_RollsBackAndAllowsNextRequestInSameScope()
    {
        await using var fixture = new ConsumerFixture(RuleLocalization, GeneralLocalization);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;
        CreateCustomerResponse created = await new CreateCustomerRequest { Name = "Ada" }.Handle<CreateCustomerRequest, CreateCustomerResponse>(sp);

        // Customer.Rename enforces its domain invariant before repository.Update.
        Response failed = await new RenameCustomerRequest { Id = created.Id, Name = "" }.Handle<RenameCustomerRequest, Response>(sp);

        Assert.Equal(HttpStatusCode.InternalServerError, failed.Status);
        Assert.False(sp.GetRequiredService<IUnitOfWork<DatabaseContext>>().TransactionEnabled);
        Customer? customer = await sp.GetRequiredService<ICustomerRepository>().FirstOrDefault(created.Id);
        Assert.Equal("Ada", customer!.Name);
        Response next = await new RenameCustomerRequest { Id = created.Id, Name = "Grace" }.Handle<RenameCustomerRequest, Response>(sp);
        Assert.Equal(HttpStatusCode.OK, next.Status);
        Assert.Equal("Grace", (await sp.GetRequiredService<ICustomerRepository>().FirstOrDefault(created.Id))!.Name);
    }

    [Fact]
    public async Task MissingEntity_EarlyReturnDisposesTransaction()
    {
        await using var fixture = new ConsumerFixture(RuleLocalization, GeneralLocalization);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;

        Response response = await new DeleteCustomerRequest { Id = Guid.NewGuid() }.Handle<DeleteCustomerRequest, Response>(sp);

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.False(sp.GetRequiredService<IUnitOfWork<DatabaseContext>>().TransactionEnabled);
    }
}
