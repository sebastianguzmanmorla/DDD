using System.Net;
using Consumer.Contracts.Messaging.Customers;
using Consumer.Domain.Entities.Customers;
using Consumer.Domain.Interfaces.Repositories;
using Consumer.Infrastructure;
using SebastianGuzmanMorla.DDD.Domain.Messaging;
using SebastianGuzmanMorla.DDD.Infrastructure.Handlers;

namespace Consumer.Application.Handlers.Customers;

public sealed class RenameCustomerRequestHandler(IServiceProvider services, ICustomerRepository customers)
    : RequestHandler<DatabaseContext, RenameCustomerRequest, Response>(services)
{
    protected override async Task<Response> Execute(RenameCustomerRequest request, CancellationToken cancellationToken = default)
    {
        await UnitOfWork.CreateTransaction(cancellationToken);
        try
        {
            Customer? customer = await customers.FirstOrDefault(request.Id, cancellationToken);
            if (customer is null) return new Response { Status = HttpStatusCode.NotFound };
            customer.Rename(request.Name);
            await customers.Update(cancellationToken, customer);
            await UnitOfWork.Commit(cancellationToken);
            return new Response();
        }
        catch
        {
            await UnitOfWork.Rollback(cancellationToken);
            throw;
        }
    }
}

public sealed class DeleteCustomerRequestHandler(IServiceProvider services, ICustomerRepository customers)
    : RequestHandler<DatabaseContext, DeleteCustomerRequest, Response>(services)
{
    protected override async Task<Response> Execute(DeleteCustomerRequest request, CancellationToken cancellationToken = default)
    {
        await UnitOfWork.CreateTransaction(cancellationToken);
        try
        {
            Customer? customer = await customers.FirstOrDefault(request.Id, cancellationToken);
            if (customer is null) return new Response { Status = HttpStatusCode.NotFound };
            await customers.SoftDelete(cancellationToken, customer);
            await UnitOfWork.Commit(cancellationToken);
            return new Response();
        }
        catch
        {
            await UnitOfWork.Rollback(cancellationToken);
            throw;
        }
    }
}
