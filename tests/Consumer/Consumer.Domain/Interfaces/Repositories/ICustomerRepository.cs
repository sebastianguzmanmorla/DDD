using Consumer.Domain.Entities.Customers;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;

namespace Consumer.Domain.Interfaces.Repositories;

public interface ICustomerRepository : IRepository<Customer>
{
    Task<bool> NameExists(string name, CancellationToken cancellationToken = default);
}
