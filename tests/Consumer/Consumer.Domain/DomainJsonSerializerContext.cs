using System.Text.Json.Serialization;
using Consumer.Domain.Entities.Customers;

namespace Consumer.Domain;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Customer))]
[JsonSerializable(typeof(List<Customer>))]
public partial class DomainJsonSerializerContext : JsonSerializerContext;
