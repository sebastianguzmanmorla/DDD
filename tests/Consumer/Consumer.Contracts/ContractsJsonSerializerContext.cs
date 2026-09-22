using System.Text.Json.Serialization;
using Consumer.Contracts.Messaging.Customers;
using Consumer.Contracts.Data.Customers;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace Consumer.Contracts;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CreateCustomerRequest))]
[JsonSerializable(typeof(CreateCustomerResponse))]
[JsonSerializable(typeof(RenameCustomerRequest))]
[JsonSerializable(typeof(DeleteCustomerRequest))]
[JsonSerializable(typeof(Response))]
[JsonSerializable(typeof(GetCustomersRequest))]
[JsonSerializable(typeof(GetCustomersResponse))]
[JsonSerializable(typeof(CustomerData))]
[JsonSerializable(typeof(List<CustomerData>))]
public partial class ContractsJsonSerializerContext : JsonSerializerContext;
