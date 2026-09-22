using System.Text.Json.Serialization;
using SebastianGuzmanMorla.DDD.Domain.Entities;

namespace Consumer.Domain.Entities.Customers;

public sealed class Customer : Entity
{
    public string Name { get; private set; }

    [JsonConstructor]
    public Customer(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }
}
