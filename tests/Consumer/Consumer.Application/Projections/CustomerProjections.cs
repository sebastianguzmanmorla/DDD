using System.Linq.Expressions;
using Consumer.Contracts.Data.Customers;
using Consumer.Domain.Entities.Customers;

namespace Consumer.Application.Projections;

public static class CustomerProjections
{
    public static Expression<Func<Customer, CustomerData>> ToDataExpression =>
        customer => new CustomerData(customer.Id, customer.Name);
}
