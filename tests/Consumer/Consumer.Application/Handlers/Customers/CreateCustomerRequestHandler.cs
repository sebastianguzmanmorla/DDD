using Consumer.Contracts.Messaging.Customers;
using Consumer.Domain.Entities.Customers;
using Consumer.Domain.Interfaces.Repositories;
using Consumer.Infrastructure;
using SebastianGuzmanMorla.DDD.Infrastructure.Handlers;

namespace Consumer.Application.Handlers.Customers;

public sealed class CreateCustomerRequestHandler(IServiceProvider services, ICustomerRepository customers)
    : RequestHandler<DatabaseContext, CreateCustomerRequest, CreateCustomerResponse>(services)
{
    protected override async Task<CreateCustomerResponse> Execute(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        await UnitOfWork.CreateTransaction(cancellationToken);
        try
        {
            var customer = new Customer(request.Name!);
            await customers.Add(cancellationToken, customer);
            await UnitOfWork.Commit(cancellationToken);
            return new CreateCustomerResponse { Id = customer.Id };
        }
        catch
        {
            await UnitOfWork.Rollback(cancellationToken);
            throw;
        }
    }
}
