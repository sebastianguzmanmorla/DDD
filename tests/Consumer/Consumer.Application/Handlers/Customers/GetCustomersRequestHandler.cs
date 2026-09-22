using Consumer.Application.Projections;
using Consumer.Contracts.Data.Customers;
using Consumer.Contracts.Messaging.Customers;
using Consumer.Domain.Entities.Customers;
using Consumer.Infrastructure;
using SebastianGuzmanMorla.DDD.Infrastructure.Handlers;

namespace Consumer.Application.Handlers.Customers;

public sealed class GetCustomersRequestHandler(IServiceProvider services)
    : RequestPageHandler<DatabaseContext, GetCustomersRequest, GetCustomersResponse, Customer, CustomerData>(services)
{
    protected override IQueryable<CustomerData> PageQuery(GetCustomersRequest request) =>
        Queryable.OrderBy(customer => customer.Name).Select(CustomerProjections.ToDataExpression);
}
