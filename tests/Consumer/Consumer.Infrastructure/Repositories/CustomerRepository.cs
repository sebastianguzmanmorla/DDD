using Consumer.Domain.Entities.Customers;
using Consumer.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using SebastianGuzmanMorla.DDD.Infrastructure.Repositories;

namespace Consumer.Infrastructure.Repositories;

public sealed class CustomerRepository(IServiceProvider services)
    : Repository<DatabaseContext, Customer>(services), ICustomerRepository
{
    public Task<bool> NameExists(string name, CancellationToken cancellationToken = default) =>
        Queryable.AnyAsync(x => x.Name == name, cancellationToken);
}
