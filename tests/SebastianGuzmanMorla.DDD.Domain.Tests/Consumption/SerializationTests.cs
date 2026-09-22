using System.Text.Json;
using Consumer.Contracts;
using Consumer.Contracts.Messaging.Customers;
using Consumer.Domain;
using Consumer.Domain.Entities.Customers;

namespace SebastianGuzmanMorla.DDD.Domain.Tests.Consumption;

public class SerializationTests
{
    [Fact]
    public void AuditClone_UsesGeneratedRedactionAndPreservesOriginalRequest()
    {
        var original = new CreateCustomerRequest { Name = "Ada", Secret = "do-not-log" };
        var clone = (CreateCustomerRequest)original.Clone();

        clone.ClearSensitiveProperties();
        string auditJson = JsonSerializer.Serialize(clone, ContractsJsonSerializerContext.Default.CreateCustomerRequest);

        Assert.DoesNotContain("do-not-log", auditJson);
        Assert.DoesNotContain("secret", auditJson);
        Assert.Contains("Ada", auditJson);
        Assert.Equal("do-not-log", original.Secret);
    }

    [Fact]
    public void GeneratedJsonMetadata_RoundTripsRichDomainEntityAndList()
    {
        var customer = new Customer("Ada");
        customer.Rename("Grace");
        string json = JsonSerializer.Serialize(customer, DomainJsonSerializerContext.Default.Customer);

        Customer restored = JsonSerializer.Deserialize(json, DomainJsonSerializerContext.Default.Customer)!;

        Assert.Equal(customer.Id, restored.Id);
        Assert.Equal("Grace", restored.Name);
        var listJson = JsonSerializer.Serialize(new List<Customer> { customer }, DomainJsonSerializerContext.Default.ListCustomer);
        Assert.Equal(customer.Id, Assert.Single(JsonSerializer.Deserialize(listJson, DomainJsonSerializerContext.Default.ListCustomer)!).Id);
    }
}
