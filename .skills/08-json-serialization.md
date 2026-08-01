# 8. System.Text.Json Serialization Contexts

To achieve reflection-free high-performance JSON serialization and Native AOT compatibility, every bounded context uses System.Text.Json Source Generation contexts.

---

## 1. Context Declaration & Source Generation Options

Declare a partial `JsonSerializerContext` class in Contracts and Domain layers decorated with standard `[JsonSourceGenerationOptions]`:

```csharp
using System.Text.Json.Serialization;

namespace MyProject.Contracts;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    IgnoreReadOnlyFields = true,
    IgnoreReadOnlyProperties = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
public partial class ContractsJsonSerializerContext : JsonSerializerContext;
```

```csharp
using System.Text.Json.Serialization;

namespace MyProject.Domain;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    IgnoreReadOnlyFields = true,
    IgnoreReadOnlyProperties = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
public partial class DomainJsonSerializerContext : JsonSerializerContext;
```

---

## 2. Rules for Including Serializable Types

Every new type created in the solution **MUST** be registered with `[JsonSerializable(typeof(T))]` in the appropriate context file:

### A. Contracts Context (`ContractsJsonSerializerContext.cs`)
Must annotate:
* **All DTOs / Data Models**: e.g. `[JsonSerializable(typeof(CustomerData))]`
* **Collection DTOs**: e.g. `[JsonSerializable(typeof(List<CustomerData>))]`
* **All Requests**: e.g. `[JsonSerializable(typeof(CreateCustomerRequest))]`
* **All Responses**: e.g. `[JsonSerializable(typeof(CreateCustomerResponse))]`
* **Security & Identity Contexts**: e.g. `[JsonSerializable(typeof(IdentityContext))]`

### B. Domain Context (`DomainJsonSerializerContext.cs`)
Must annotate:
* **All Domain Entities**: e.g. `[JsonSerializable(typeof(Customer))]`
* **Generic Entity Lists (Required for `CachedRepository` Redis caching)**: e.g. `[JsonSerializable(typeof(List<Customer>))]`
* **Custom Value Objects**: e.g. `[JsonSerializable(typeof(Address))]`
* **Domain Notifications / Events**: e.g. `[JsonSerializable(typeof(WelcomeNotification))]`

---

## 3. Configuring Web Pipeline Bootstrapping (`Program.cs`)

Register both generated contexts in ASP.NET Core `HttpJsonOptions` TypeInfoResolverChain so Minimal APIs and Minimal API `group.MapRequest` endpoints serialize using reflection-free code:

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ContractsJsonSerializerContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Insert(1, DomainJsonSerializerContext.Default);
});
```
