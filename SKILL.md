---
name: ddd_development
description: Guidelines and code templates for developing Domain-Driven Design (DDD) components. Use this when creating entities, value objects, CQRS handlers, requests/responses, repositories, or extending bounded contexts.
---

# Domain-Driven Design (DDD) Development Skill

This skill guides development inside DDD-structured codebases, aligning with the architecture established by the `SebastianGuzmanMorla.DDD`, `SebastianGuzmanMorla.Validator`, and `SebastianGuzmanMorla.SmartEnum` libraries.

---

## Architectural Layers

Codebases are typically organized into four main layers per bounded context:
1. **Domain Layer (`[Project].Domain`)**: Pure domain models (Entities, Value Objects), interfaces, domain validators, and domain events/notifications. Free of infrastructure dependencies.
2. **Contracts Layer (`[Project].Contracts`)**: DTOs, request/response message schemas, shared enums, and basic syntactic validators. Distributed to and consumed by external clients.
3. **Application Layer (`[Project].Application`)**: Handlers implementing CQRS patterns (Commands/Queries), application services, projections, and process managers.
4. **Infrastructure Layer (`[Project].Infrastructure`)**: EF Core DbContext, entity configurations, repository implementations, migrations, and external service adapters.

---

## 1. Domain Layer (`[Project].Domain`)

### Creating Entities
All domain entities must inherit from `SebastianGuzmanMorla.DDD.Domain.Entities.Entity`.

The base `Entity` class provides the following out of the box:
- `Id`: A UUID Version 7 (sequential GUID) generated automatically using `Guid.CreateVersion7()`.
- `CreatedAt`: Automatically initialized to `DateTime.UtcNow`.
- `UpdatedAt`: Initialized to `DateTime.UtcNow` (must be updated when mutating).
- `DeletedAt`: Soft delete timestamp (`DateTime?`).

Example:
```csharp
using SebastianGuzmanMorla.DDD.Domain.Entities;

namespace MyProject.Domain.Entities;

public class SampleEntity : Entity
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    
    // Add domain behavior (methods) rather than just anemic getters/setters when applicable
    public void UpdateDescription(string? newDescription)
    {
        Description = newDescription;
        UpdatedAt = DateTime.UtcNow;
    }
}
```

### Soft Deleting Entities
Instead of calling EF Core's `Remove`, perform a soft delete by setting `DeletedAt` and updating `UpdatedAt`:
```csharp
public void Delete()
{
    DeletedAt = DateTime.UtcNow;
    UpdatedAt = DateTime.UtcNow;
}
```
In your Handlers, after soft-deleting, log the event using `LogType.Delete`:
```csharp
entity.Delete();
AddEntityLog(LogType.Delete, entity, "Soft-deleted sample entity");
```

* **RULE**: Because global query filters for soft deletion are not enabled by default, any manual query that bypasses `RequestPageHandler` (e.g., custom repository queries) must explicitly filter soft-deleted entities using `.Where(x => x.DeletedAt == null)`.

---

## 2. Smart Enums (`SebastianGuzmanMorla.SmartEnum`)

Use `SmartEnum` instead of native C# enums to associate values and behavior with enum choices.

### Single Smart Enum (`SmartEnum<TEnum, TKey>`)
Inherit from `SmartEnum<TEnum, TKey>`. Annotate with `[JsonConverter(typeof(SmartEnumJsonConverter<TEnum, TKey>))]` and `[GenerateSmartEnum]`. Mark the class as `partial`.

```csharp
using System.Text.Json.Serialization;
using SebastianGuzmanMorla.SmartEnum;
using SebastianGuzmanMorla.SmartEnum.Attributes;
using SebastianGuzmanMorla.SmartEnum.Converters.Json;

namespace MyProject.Contracts.Data.Enums;

[JsonConverter(typeof(SmartEnumJsonConverter<StatusType, string>))]
[GenerateSmartEnum]
public sealed partial class StatusType : SmartEnum<StatusType, string>
{
    public static readonly StatusType Active = new("active");
    public static readonly StatusType Suspended = new("suspended");

    private StatusType(string value) : base(value)
    {
    }
}
```

### Flag Smart Enum (`SmartEnumFlags<TFlags, TEnum, TKey>`)
For collections or bitwise flags of a `SmartEnum`, inherit from `SmartEnumFlags`. Annotate with `[JsonConverter(typeof(SmartEnumFlagsJsonConverter<TFlags, TEnum, TKey>))]`.

```csharp
using System.Text.Json.Serialization;
using SebastianGuzmanMorla.SmartEnum;
using SebastianGuzmanMorla.SmartEnum.Converters.Json;

namespace MyProject.Contracts.Data.Enums;

[JsonConverter(typeof(SmartEnumFlagsJsonConverter<UserRoles, RoleType, string>))]
public sealed class UserRoles : SmartEnumFlags<UserRoles, RoleType, string>
{
    public static readonly UserRoles AdminRoles = new(RoleType.Admin, RoleType.Manager);

    public UserRoles(params RoleType[] flags) : base(flags) { }
    public UserRoles() { }
}
```

---

## 3. Validation (`SebastianGuzmanMorla.Validator`)

Validation is divided into two phases to enforce architectural boundaries:

### A. Contracts Layer (`[Project].Contracts/Validators`)
Handles syntactic/basic validation (e.g., checks for nulls, empty collections, regex match, emails).
* **RULE**: Contract validators must *never* use repositories, DB contexts, or access external services. They must inherit from `Validator<T>`.

```csharp
using SebastianGuzmanMorla.Validator;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace MyProject.Contracts.Validators;

public class SampleValidator : Validator<ISampleValidation>
{
    public SampleValidator()
    {
        RuleFor(x => x.Email)
            .NotNull((x, _) => x.GetRequiredService<IRuleLocalization>().NotNull(nameof(ISampleValidation.Email)), ValidationErrorHandle.StopProperty)
            .NotEmpty((x, _) => x.GetRequiredService<IRuleLocalization>().NotEmpty(nameof(ISampleValidation.Email)), ValidationErrorHandle.StopProperty);
    }
}
```

### B. Domain Layer (`[Project].Domain/Validators`)
Handles semantic/business rule validation (e.g., database uniqueness checks, status verification).
* **RULE**: Inherits from the contract validator and utilizes `Must`, `RuleForWhen`, or `ValidateEntity` with dependency-injected repositories.

```csharp
using MyProject.Contracts.Interfaces;
using MyProject.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.Validator;

namespace MyProject.Domain.Validators;

public class SampleValidator : Contracts.Validators.SampleValidator
{
    public SampleValidator()
    {
        RuleFor(x => x.Email)
            .Must(EmailDoesNotExist, (x, _) =>
            {
                string label = x.GetRequiredService<IGeneralLocalization>().Email;
                return x.GetRequiredService<IRuleLocalization>().AlreadyExists(label);
            }, ValidationErrorHandle.StopAll);
    }

    private static Task<bool> EmailDoesNotExist(IServiceProvider provider, ISampleValidation entity, CancellationToken cancellationToken = default)
    {
        // Must return true if it is valid (i.e. email does not exist)
        return provider.GetRequiredService<ISampleRepository>().None(entity.Email, cancellationToken);
    }
}
```

### Built-in Validation Rules & Flow Control

#### Built-in Rules
The custom validator package exposes several chainable extension methods on `RuleFor` to perform common assertions:

| Rule | Supported Types | Description |
|---|---|---|
| `NotEmpty` | `string`, `Guid`, `Guid?`, arrays/collections | Validates that string/array is not empty, or Guid is not empty (`Guid.Empty`). |
| `NotNull` | Any object reference | Validates that the property is not `null`. |
| `MinimumLength(min)` | `string` | Asserts string length is at least `min`. |
| `MaximumLength(max)` | `string` | Asserts string length is at most `max`. |
| `EmailAddress` | `string` | Asserts the value is a valid email format. |
| `Equal(value/expression)`| Comparable types | Asserts equality against a constant or another property expression. |
| `NotEqual(value/expression)`| Comparable types | Asserts inequality against a constant or another property expression. |
| `Minimum(min)` | Comparable types | Asserts property is greater than or equal to `min`. |
| `Maximum(max)` | Comparable types | Asserts property is less than or equal to `max`. |
| `Between(min, max)` | Comparable types | Asserts property falls within the `[min, max]` range. |
| `Matches(regex)` | `string` | Asserts property matches the specified regular expression. |
| `Must(expression)` | Any type | Runs a custom asynchronous validation delegate returning a boolean. |

#### Validation Flow Control (`ValidationErrorHandle`)
Every validation rule accepts an optional `ValidationErrorHandle` parameter to configure the flow execution after a failure:

* **`ValidationErrorHandle.Continue` (Default)**: If this rule fails, record the error and continue executing subsequent rules for this property and other properties.
* **`ValidationErrorHandle.StopProperty`**: If this rule fails, record the error and abort executing any remaining rules *for this property* (e.g. if `NotNull` fails, skip running `NotEmpty` or `EmailAddress` checks on it), but proceed validating other properties.
* **`ValidationErrorHandle.StopAll`**: If this rule fails, record the error and immediately halt *the entire validation process* for this validator instance, returning immediately.

### Advanced Validation Features

#### Conditional Validation (`RuleForWhen`)
Runs validation rules based on a dynamic asynchronous runtime evaluation:
```csharp
RuleForWhen(u => u.PromoCode, (provider, user, ct) => Task.FromResult(user.HasOptedIn))
    .NotEmpty((x, _) => "Promo code is required if opted in");
```

#### Nested Entities & Collections Validation (`ValidateEntity`)
Enables cascading validations to validate nested complex types or lists. The validator for the nested type is resolved automatically from the DI container:
```csharp
// Validates nested properties using registered validators
ValidateEntity(c => c.BillingAddress);
ValidateEntity(c => c.LineItems); // Works for collections as well
```

#### Reusable Interface Validation Pattern (Interface Cascading)
To avoid duplicating validation rules across different requests and entities that share common fields (like `ClientId` or `UserId`), the solution uses validation interfaces:

1. **Define the Interface (`[Project].Contracts/Interfaces`)**: Create an interface representing the field(s) to validate, which inherits from `IEntityValidation`:
   ```csharp
   using SebastianGuzmanMorla.Validator.Interfaces;
   
   namespace MyProject.Contracts.Interfaces;
   
   public interface IClientIdValidation : IEntityValidation
   {
       public Guid ClientId { get; set; }
   }
   ```
2. **Implement rules on the Interface Validator**: Write a validator targeting the interface type:
   ```csharp
   public class ClientIdValidator : Validator<IClientIdValidation> { ... }
   ```
3. **Implement the Interface on target models**: Mark your CQRS requests or domain entities as implementing the validation interface:
   ```csharp
   public class CreateClientRequest : Request<CreateClientResponse>, IClientIdValidation
   {
       public Guid ClientId { get; set; }
   }
   ```
4. **Mark concrete validators as partial**: Ensure the concrete validator is declared `partial`. The Roslyn Source Generator will automatically find all validation interfaces implemented by the model and chain their validations into the concrete validator's execution flow:
   ```csharp
   public partial class CreateClientRequestValidator : Validator<CreateClientRequest>
   {
       // The source generator automatically generates code to execute IValidator<IClientIdValidation>.Validate(...)
   }
   ```

---

## 4. Application Layer & CQRS (`SebastianGuzmanMorla.DDD`)

### CQRS Message Schemas
Create Requests and Responses in `[Project].Contracts/Messaging`.
* Commands and queries inherit from `Request<TResponse>`.
* Responses inherit from `Response`.
* **RULE (Redacting Sensitive Data)**: Properties that contain credentials, passwords, or secret tokens **MUST** be decorated with `[SensitiveData]` (from `SebastianGuzmanMorla.DDD.Domain.Attributes`).
* **RULE (Partial Requirement)**: Any Request containing sensitive properties must be declared as a `partial class` so the source generator can implement the property redaction code.

```csharp
using System.Text.Json.Serialization;
using SebastianGuzmanMorla.DDD.Domain.Attributes;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace MyProject.Contracts.Messaging.Sample;

public partial class CreateSampleRequest : Request<CreateSampleResponse>
{
    public required string Name { get; set; }

    [SensitiveData] // Automatically cleared from logs
    public required string Password { get; set; }
}

public class CreateSampleResponse : Response
{
    public Guid Id { get; set; }
}
```


### CQRS Handlers
Create handlers under `[Project].Application/Handlers`. Handlers inherit from `RequestHandler<TContext, TRequest, TResponse>`.
* Use the primary constructor to take `IServiceProvider serviceProvider` and pass it to the base constructor.
* Locate repositories and application dependencies inside the handler using `serviceProvider.GetRequiredService<T>()`.
* Manage transactions explicitly inside a `using (UnitOfWork)` block or via the base handler workflow hooks (`OnAfterExecute`).

```csharp
using MyProject.Contracts.Messaging.Sample;
using MyProject.Domain.Entities;
using SebastianGuzmanMorla.DDD.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace MyProject.Application.Handlers.Sample;

public class CreateSampleRequestHandler(
    IServiceProvider serviceProvider
) : RequestHandler<MyDbContext, CreateSampleRequest, CreateSampleResponse>(serviceProvider)
{
    private readonly IRepository<SampleEntity> _sampleRepository = serviceProvider.GetRequiredService<IRepository<SampleEntity>>();

    protected override async Task<CreateSampleResponse> Execute(CreateSampleRequest request, CancellationToken cancellationToken)
    {
        var entity = new SampleEntity { Name = request.Name };

        await _sampleRepository.Add(cancellationToken, entity);
        
        // Audit log helpers (use LogType.Put for creation and updates)
        AddEntityLog(LogType.Put, entity, "Created new sample entity");

        return new CreateSampleResponse { Id = entity.Id };
    }
}
```

### Aggregate-Specific Base Handlers
To reduce repetitive repository resolution calls (`serviceProvider.GetRequiredService<T>()`), bounded contexts provide specialized abstract handlers that pre-inject all relevant aggregate repositories. Use these instead of the raw `RequestHandler` when developing features for specific aggregates:

```csharp
using MyProject.Application.Handlers;
using MyProject.Domain.Interfaces.Repositories;
using MyProject.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace MyProject.Application.Handlers.Customers;

public abstract class CustomerHandler<TRequest, TResponse>(
    IServiceProvider serviceProvider
) : RequestHandler<MyDbContext, TRequest, TResponse>(serviceProvider)
    where TRequest : Request<TResponse>
    where TResponse : Response, new()
{
    protected readonly ICustomerRepository CustomerRepository = serviceProvider.GetRequiredService<ICustomerRepository>();
    protected readonly IAddressRepository AddressRepository = serviceProvider.GetRequiredService<IAddressRepository>();
}
```

### CQRS Handler Execution Workflow
The base `RequestHandler` automatically manages the execution lifecycle of requests:
1. **Automated Request Validation**: Before executing the handler, it resolves `IValidator<TRequest>` from the DI container (if registered). If validation fails, it stops execution and automatically returns a `400 BadRequest` response with the collection of validation errors.
2. **Execute**: Calls your overridden `Execute(TRequest request, CancellationToken cancellationToken)`.
3. **OnAfterExecute**: Hook to perform post-execution actions, such as automatically saving audit logs.
4. **Notification Dispatch**: Iterates over all notifications registered during execution and runs them.

### Domain Notifications & Events
Notifications represent side-effects or domain events (e.g., sending emails, raising external messages) that should execute asynchronously after the core database transaction is successfully committed.

#### 1. Define Notification (`[Project].Domain/Notifications`)
Inherit from `INotification` and implement the double-dispatch `Handle` pattern:
```csharp
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;

namespace MyProject.Domain.Notifications;

public class WelcomeNotification : INotification
{
    public required string Email { get; init; }
    public DateTime Timestamp { get; } = DateTime.UtcNow;

    public Task Handle(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        // Resolve target handler and dispatch execution
        var handler = serviceProvider.GetRequiredService<INotificationHandler<WelcomeNotification>>();
        return handler.Handle(this, cancellationToken);
    }
}
```

#### 2. Implement Notification Handler (`[Project].Application/Notifications`)
Inherit from `INotificationHandler<TNotification>` to implement the execution side effect:
```csharp
using MyProject.Domain.Notifications;
using Microsoft.Extensions.Logging;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;

namespace MyProject.Application.Notifications;

public class WelcomeNotificationHandler(
    ILogger<WelcomeNotificationHandler> logger
) : INotificationHandler<WelcomeNotification>
{
    public async Task Handle(WelcomeNotification notification, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Welcome email dispatched to {Email}", notification.Email);
        await Task.CompletedTask;
    }
}
```

#### 3. Raise Notifications inside CQRS Handlers
Register the notification inside the `Execute` method using `AddNotification`:
```csharp
AddNotification(new WelcomeNotification { Email = request.Email });
```
* **RULE**: Notifications must only be raised if the core operation completes successfully. The base handler will execute them *after* the handler has run successfully.

---

### Paginated Queries (`RequestPageHandler`)
For listing and searching data, use `RequestPageHandler<TContext, TRequest, TResponse, TEntity, TResponseEntity>`.
* **Note**: `RequestPageHandler` automatically filters out soft-deleted entities (`DeletedAt == null`) and performs asynchronous `CountAsync()`, `Skip()`, and `Take()` operations on the pagination dataset.
* Implement query sorting, filters, and projections (to DTOs) by overriding `PageQuery(TRequest request)`.

#### 1. Paginated Request & Response
```csharp
using SebastianGuzmanMorla.DDD.Domain.Attributes;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace MyProject.Contracts.Messaging.Customers;

[LogIgnore] // Typically query handlers should bypass audit logs using [LogIgnore]
public class GetCustomersRequest : RequestPage<GetCustomersResponse>
{
    public const string Route = "/Customers";
    public const RequestMethod Method = RequestMethod.Get;

    public string? Name { get; set; }
}

public class GetCustomersResponse : ResponsePage<CustomerData>;
```

#### 2. Paginated Handler Implementation
```csharp
using MyProject.Application.Handlers;
using MyProject.Application.Projections;
using MyProject.Contracts.Data;
using MyProject.Domain.Entities;
using MyProject.Infrastructure;
using MyProject.Contracts.Messaging.Customers;

namespace MyProject.Application.Handlers.Customers;

public class GetCustomersRequestHandler(
    IServiceProvider serviceProvider
) : RequestPageHandler<MyDbContext, GetCustomersRequest, GetCustomersResponse, Customer, CustomerData>(serviceProvider)
{
    protected override IQueryable<CustomerData> PageQuery(GetCustomersRequest request)
    {
        IQueryable<Customer> query = Queryable; // Base property referencing MyDbContext.Set<Customer>()

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            query = query.Where(x => x.Name.Contains(request.Name));
        }

        return query
            .OrderBy(x => x.Name)
            .Select(CustomerProjections.ToCustomerDataExpression); // Map Customer entity to CustomerData DTO
    }
}
```

---

### HTTP Model Binding (`IRequestBinder`)
For complex requests (like parsing values from form bodies, query parameters, or authorization headers), implement a model binder in `[Project].Web/Binders` instead of manual parsing in controllers/endpoints.
* Custom binders must implement `IRequestBinder<TRequest, TErrorResponse>` from `SebastianGuzmanMorla.DDD.Interfaces`.
* The binder resolves the request components, parses data, handles `SmartEnum` conversion exceptions, and returns the constructed request object or an error response.
* **Note**: Binders are registered in the DI container automatically by the source generator.

---

## 5. Infrastructure Layer (`[Project].Infrastructure`)

### EF Core Mappings for SmartEnums
In the `ConfigureConventions` method of your DbContext class, register converters for `SmartEnum` and `SmartEnumFlags` properties:

```csharp
using Microsoft.EntityFrameworkCore;
using SebastianGuzmanMorla.SmartEnum.EntityFrameworkCore.Converters;

namespace MyProject.Infrastructure;

public class MyDbContext(DbContextOptions<MyDbContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Mapping Single SmartEnum
        configurationBuilder.Properties<StatusType>()
            .HaveConversion<SmartEnumConverter<StatusType, string>, SmartEnumComparer<StatusType, string>>()
            .HaveColumnType("text");

        // Mapping SmartEnumFlags
        configurationBuilder.Properties<UserRoles>()
            .HaveConversion<SmartEnumFlagsValueConverter<UserRoles, RoleType, string>, SmartEnumFlagsValueComparer<UserRoles, RoleType, string>>()
            .HaveColumnType("text");
    }
}
```

### PostgreSQL Native Enum Mapping Pattern
When storing standard C# enums (like `LogType`) as native database enums in PostgreSQL using Npgsql, configure the mappings in three locations:

1. **In `ConfigureConventions` of your DbContext**:
   Map the C# enum type to the PostgreSQL database type name:
   ```csharp
   protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
   {
       base.ConfigureConventions(configurationBuilder);
   
       configurationBuilder.Properties<MyEnum>()
           .HaveColumnType("my_enum_db_type");
   }
   ```

2. **In `OnModelCreating` of your DbContext**:
   Register the enum type in the PostgreSQL database schema:
   ```csharp
   protected override void OnModelCreating(ModelBuilder builder)
   {
       base.OnModelCreating(builder);
   
       builder.HasPostgresEnum<MyEnum>(name: "my_enum_db_type");
   }
   ```

3. **In the DbContext Bootstrapping (`Program.cs` / `ConfigureInfrastructure`)**:
   Map the enum inside the `UseNpgsql` options builder callback using a static method in your DbContext class:
   ```csharp
   // Define a static mapping method in your DbContext class:
   public static void MapEnums(NpgsqlDbContextOptionsBuilder builder)
   {
       builder.MapEnum<MyEnum>("my_enum_db_type");
   }
   
   // Pass it to UseNpgsql during service configuration:
   services.ConfigureInfrastructure(options =>
   {
       options.UseNpgsql(connectionString, MyDbContext.MapEnums);
   });
   ```

### Entity Configuration Mappings
Keep entity mappings clean by implementing `IEntityTypeConfiguration<TEntity>` in separate mapping files inside `[Project].Infrastructure/Mappings`, naming them `[EntityName]Map.cs`.
* **RULE**: Inside the `Configure(EntityTypeBuilder<T> builder)` method, you **MUST** call the extension method `builder.ConfigureEntity();` from `SebastianGuzmanMorla.DDD.Infrastructure.Mappings` to automatically configure the base `Entity` properties (primary key, CreatedAt, UpdatedAt, and DeletedAt columns).

Example (`SampleEntityMap.cs`):
```csharp
using MyProject.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SebastianGuzmanMorla.DDD.Infrastructure.Mappings;

namespace MyProject.Infrastructure.Mappings;

public class SampleEntityMap : IEntityTypeConfiguration<SampleEntity>
{
    public void Configure(EntityTypeBuilder<SampleEntity> builder)
    {
        builder.ToTable(nameof(SampleEntity));

        builder.Property(x => x.Name)
            .HasMaxLength(100)
            .IsRequired();

        // Configure base Entity fields (Id, CreatedAt, UpdatedAt, DeletedAt) automatically
        builder.ConfigureEntity();
    }
}
```

### Auto-Applying Mappings in DbContext
Instead of registering each entity configuration mapping class manually, call the generated extension method `builder.ApplyGeneratedConfigurations();` inside the DbContext's `OnModelCreating` method:

```csharp
protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder);
    
    // Automatically discovers and registers all mapping classes in the assembly
    builder.ApplyGeneratedConfigurations();
}
```

### EF Core Compiled Models (Optimization for Native AOT)
To eliminate startup overhead and ensure full Native AOT compatibility, the solution compiles the EF Core database model, generating static accessors that bypass runtime reflection.

#### A. Generating the Compiled Model
Whenever database migrations are added or model mappings are modified, compile the model using the EF Core CLI tool:
```bash
dotnet ef dbcontext optimize --nativeaot --project src/MyProject.Infrastructure/MyProject.Infrastructure.csproj --startup-project src/MyProject.Web/MyProject.Web.csproj --context MyDbContext
```
This generates compiled code files under `[Project].Infrastructure/CompiledModels/`.

#### B. Registering the Compiled Model
Load the static model instance when configuring the DbContext options in your bootstrapper:
```csharp
builder.Services.ConfigureInfrastructure(options =>
{
    options.UseNpgsql(connectionString)
           // Load the compiled static model representation
           .UseModel(MyDbContextModel.Instance); 
});
```

### Implementing Repositories (`[Project].Infrastructure/Repositories`)
Define repository interfaces in the Domain layer (`[Project].Domain/Interfaces/Repositories`) and implement them in the Infrastructure layer. The solution supports two repository patterns:

#### A. Standard Repository (`Repository<TContext, TEntity>`)
Use this for entities that do not require caching or whose state changes very frequently:
```csharp
using MyProject.Domain.Entities;
using MyProject.Domain.Interfaces.Repositories;
using SebastianGuzmanMorla.DDD.Infrastructure.Repositories;

namespace MyProject.Infrastructure.Repositories;

public class UserRepository(
    IServiceProvider serviceProvider
) : Repository<MyDbContext, User>(serviceProvider), IUserRepository
{
    // EF Core DbContext Queryable is available through the base property 'Queryable'
}
```

#### B. Cached Repository (`CachedRepository<TContext, TEntity>`)
Use this for read-heavy aggregates (like configurations, lists, and static parameters) that benefit from caching.
* **Requirement**: You must specify a `CacheKeyPrefix` and link to the source-generated JSON serializer context type info (`JsonTypeInfo<TEntity>`):
```csharp
using System.Text.Json.Serialization.Metadata;
using MyProject.Domain;
using MyProject.Domain.Entities;
using MyProject.Domain.Interfaces.Repositories;
using SebastianGuzmanMorla.DDD.Infrastructure.Repositories;

namespace MyProject.Infrastructure.Repositories;

public class CustomerRepository(
    IServiceProvider serviceProvider
) : CachedRepository<MyDbContext, Customer>(serviceProvider), ICustomerRepository
{
    protected override string CacheKeyPrefix => "MyProject:customer";

    // Link to the serialization metadata to support reflection-free serialization
    protected override JsonTypeInfo<Customer> JsonTypeInfo => DomainJsonSerializerContext.Default.Customer;
}
```

---

## 6. Source Generators Integration

This solution relies on Roslyn Source Generators to eliminate boilerplate code for service registration and validator composition.

### A. CQRS Handlers & Binders Generator (`SebastianGuzmanMorla.DDD.Generator`)
Registers all application request handlers (`RequestHandler`) and model binders (`IRequestBinder`) automatically.
* **Requirement**: Declare a static partial method to receive the generated registrations:
```csharp
namespace MyProject.Web;

public static partial class ConfigureHandlerServices
{
    private static partial void ConfigureGenerated(IServiceCollection services);

    public static IServiceCollection ConfigureBinders(this IServiceCollection services)
    {
        ConfigureGenerated(services); // Automatically registers all detected handlers and binders
        return services;
    }
}
```

### B. Infrastructure Repositories Generator (`SebastianGuzmanMorla.DDD.Generator`)
Automatically registers all repositories (`Repository` or `CachedRepository`) in the dependency injection container.
* **Requirement**: Declare a static partial method to receive the generated registrations in a partial class in your Infrastructure project:
```csharp
namespace MyProject.Infrastructure;

public static partial class ConfigureRepositoryServices
{
    private static partial void ConfigureGenerated(IServiceCollection services);

    public static IServiceCollection ConfigureInfrastructure(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> options)
    {
        // ... context configurations
        ConfigureGenerated(services); // Registers all detected repositories
        return services;
    }
}
```

### C. Validator Generator (`SebastianGuzmanMorla.Validator.Generator`)
Handles validator DI registration and cascades interface validation rules into concrete classes.
* **DI Registration**: Declare a partial method `RegisterValidators` in your DI configuration:
```csharp
public partial class ConfigureServices
{
    private static partial void RegisterValidators(IServiceCollection services);

    public static IServiceCollection ConfigureDomain(this IServiceCollection services)
    {
        RegisterValidators(services); // Automatically registers all IValidator<T> implementations
        return services;
    }
}
```
* **Chaining Interface Validations**: Concrete class validators must be marked `partial` so the generator can inject validation logic for all implemented validation interfaces (e.g., `IDeviceIdValidation` automatically cascades to `DeviceValidator`).
```csharp
public partial class DeviceValidator : Validator<Device>
{
    // Generator will automatically write code running validations for IDeviceIdValidation interface
}
```

### D. Smart Enum Generator (`SebastianGuzmanMorla.SmartEnum.Generator`)
Generates lookup, parsing, and collection properties for custom `SmartEnum` types.
* **Requirement**: Mark any class inheriting from `SmartEnum` with `[GenerateSmartEnum]` and declare it as a `partial` class:
```csharp
[GenerateSmartEnum]
public sealed partial class StatusType : SmartEnum<StatusType, string>
{
    // Generator writes the boilerplates for parsing, listing, and converters
}
```

### E. Clear Sensitive Properties Generator (`SebastianGuzmanMorla.DDD.Domain.Generator`)
Clears the values of sensitive properties on Requests before they are audited/logged.
* **Requirement**: Mark any Request inheriting property with `[SensitiveData]` and declare the class as `partial`:
```csharp
public partial class LoginRequest : Request<LoginResponse>
{
    [SensitiveData]
    public required string Password { get; set; }
}
```
* **Result**: The generator automatically creates:
```csharp
public partial class LoginRequest
{
    public override void ClearSensitiveProperties()
    {
        Password = default;
    }
}
```

---


## 7. Modular Localization

The application uses an interface-driven, modular approach to localization. Strings are grouped by feature area rather than in a single monolith.

### A. Core Localization Rules (APIs / Handlers)
When constructing messages (e.g., error results, validations), **never pass hardcoded string literals** for entity names or labels. Pass the localized property of `GeneralLocalization` instead.
* **❌ Incorrect**:
  ```csharp
  Message = RuleLocalization.NotExists("User");
  ```
* **✅ Correct**:
  ```csharp
  Message = RuleLocalization.NotExists(GeneralLocalization.User);
  ```

### B. Dummy Designer Class Pattern
Because MSBuild/dotnet CLI doesn't always trigger resource file generators during build, you must create a backing designer file with a dummy class for your localization resource to satisfy compilation.
1. Create a `.resx` resource file (e.g., `MyModuleResource.resx`).
2. Create a backing file named exactly `MyModuleResource.Designer.cs` containing:
```csharp
namespace MyProject.Localization.Resources;

public class MyModuleResource
{
}
```
3. Use the dummy class with `IStringLocalizer<MyModuleResource>` inside your localizer class implementation:
```csharp
using Microsoft.Extensions.Localization;
using MyProject.Contracts.Interfaces.Localization;
using MyProject.Localization.Resources;

namespace MyProject.Localization;

public class MyModuleLocalization(
    IStringLocalizer<MyModuleResource> localizer
) : IMyModuleLocalization
{
    public string Title => localizer[nameof(Title)];
}
```

### C. Project Localization Configuration (Bootstrap)
To configure localization in the host application:

1. **Service Registration (`Program.cs`)**:
   Add built-in localization services, register localized feature types via `ConfigureLocalization()`, and specify supported cultures:
   ```csharp
   builder.Services
       .AddLocalization()
       .ConfigureLocalization(); // Local project services extension method

   builder.Services.Configure<RequestLocalizationOptions>(options =>
   {
       List<CultureInfo> supportedCultures = [ new("en-US"), new("es-CL") ];
       options.DefaultRequestCulture = new RequestCulture("en-US", "en-US");
       options.SupportedCultures = supportedCultures;
       options.SupportedUICultures = supportedCultures;
       options.ApplyCurrentCultureToResponseHeaders = true;
   });
   ```

2. **Middleware Activation (`Program.cs`)**:
   Resolve the options and add the Request Localization middleware in the HTTP pipeline:
   ```csharp
   RequestLocalizationOptions localizationOptions =
       app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;

   app.UseRequestLocalization(localizationOptions);
   ```

3. **Register New Localizations (`ConfigureServices.cs` in `[Project].Localization`)**:
   Add any new localized module implementation classes to the transient service list:
   ```csharp
   public static class ConfigureServices
   {
       public static IServiceCollection ConfigureLocalization(this IServiceCollection services)
       {
           services
               .AddTransient<IMyModuleLocalization, MyModuleLocalization>();
           
           return services;
       }
   }
   ```

4. **Project File Configuration (`[Project].Localization.csproj`)**:
   Add compiler and embedded resource update rules so MSBuild compiles localizations and matches them to designer classes properly:
   ```xml
   <ItemGroup>
       <EmbeddedResource Update="Resources\MyModuleResource.resx">
           <Generator>PublicResXFileCodeGenerator</Generator>
           <LastGenOutput>MyModuleResource.Designer.cs</LastGenOutput>
       </EmbeddedResource>
       <Compile Update="Resources\MyModuleResource.Designer.cs">
           <DesignTime>True</DesignTime>
           <AutoGen>True</AutoGen>
           <DependentUpon>MyModuleResource.resx</DependentUpon>
       </Compile>
       <EmbeddedResource Update="Resources\MyModuleResource.es-CL.resx">
           <DependentUpon>MyModuleResource.resx</DependentUpon>
       </EmbeddedResource>
   </ItemGroup>
   ```

---

## 8. JSON Serialization Contexts (System.Text.Json Source Generation)

To support reflection-free high-performance serialization and Native AOT compatibility, the solution uses source-generated serialization contexts.

### A. Context Types
* **Domain Layer (`DomainJsonSerializerContext.cs` in `[Project].Domain`)**: Used for serializing Domain Entities, DB models, and Domain notifications/events.
* **Contracts Layer (`ContractsJsonSerializerContext.cs` in `[Project].Contracts`)**: Used for serializing DTOs, request/response models, and contract enums.

### B. Registering Types
Whenever you create a new Entity, DTO, Request, Response, or Custom Enum, you **MUST** decorate the corresponding partial context class with `[JsonSerializable(typeof(YourNewType))]`.

#### Example - Domain registration:
```csharp
namespace MyProject.Domain;

[JsonSerializable(typeof(MyNewEntity))]
[JsonSerializable(typeof(MyNewNotification))]
public partial class DomainJsonSerializerContext : JsonSerializerContext;
```

#### Example - Contracts registration:
```csharp
namespace MyProject.Contracts;

[JsonSerializable(typeof(MyNewRequest))]
[JsonSerializable(typeof(MyNewResponse))]
[JsonSerializable(typeof(MyNewDto))]
public partial class ContractsJsonSerializerContext : JsonSerializerContext;
```

---

## 9. Projection Mapping Pattern (Entity <-> DTO)

To translate entities to contract DTOs and vice-versa, create static helper classes in `[Project].Application/Projections`:
* **`Expression<Func<TEntity, TDto>>`**: Used in EF Core queries (inside `.Select()`) so that mapping evaluates on the database query execution.
* **`Func<TEntity, TDto>`**: Compiled lazily from the expression, used when mapping objects in memory.

Example implementation (`CustomerProjections`):
```csharp
using System.Linq.Expressions;
using MyProject.Contracts.Data;
using MyProject.Domain.Entities;

namespace MyProject.Application.Projections;

public static class CustomerProjections
{
    private static readonly Lazy<Func<Customer, CustomerData>> ToCustomerDataCompiled =
        new(() => ToCustomerDataExpression.Compile());

    public static Expression<Func<Customer, CustomerData>> ToCustomerDataExpression => customer => new CustomerData
    {
        Id = customer.Id,
        Name = customer.Name,
        CreatedAt = customer.CreatedAt
    };

    public static Func<Customer, CustomerData> ToCustomerData => ToCustomerDataCompiled.Value;
}
```

---

## 10. Validator Unit Testing Pattern

Tests are written using xUnit and `NSubstitute` to mock external services (such as repositories and localizations) resolved from the `IServiceProvider`.

### A. ValidatorTestBase
The test suite utilizes a base `ValidatorTestBase` class to pre-configure `IServiceProvider` mocks and stub `IRuleLocalization` methods. This ensures that validation rule errors (e.g. `RuleFor(...)` checking `.NotEmpty` or `.NotNull`) do not throw `NullReferenceException` during execution:

```csharp
using NSubstitute;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;

namespace MyProject.Tests;

public abstract class ValidatorTestBase
{
    protected readonly IGeneralLocalization GeneralLocalization;
    protected readonly IRuleLocalization RuleLocalization;
    protected readonly IServiceProvider ServiceProvider;

    protected ValidatorTestBase()
    {
        ServiceProvider = Substitute.For<IServiceProvider>();
        GeneralLocalization = Substitute.For<IGeneralLocalization>();
        RuleLocalization = Substitute.For<IRuleLocalization>();

        ServiceProvider.GetService(typeof(IGeneralLocalization)).Returns(GeneralLocalization);
        ServiceProvider.GetService(typeof(IRuleLocalization)).Returns(RuleLocalization);

        // Stub standard localized strings to prevent nulls
        RuleLocalization.NotEmpty(Arg.Any<string>()).Returns(x => $"{x.Arg<string>()} is empty");
        RuleLocalization.NotNull(Arg.Any<string>()).Returns(x => $"{x.Arg<string>()} is null");
        RuleLocalization.MaximumLength(Arg.Any<string>(), Arg.Any<int>())
            .Returns(x => $"{x.Arg<string>()} max length is {x.ArgAt<int>(1)}");
        RuleLocalization.AlreadyExists(Arg.Any<string>()).Returns(x => $"{x.Arg<string>()} already exists");
    }
}
```

### B. Private Test Classes & Test Cases
Declare a small private mock class implementing your target validation interface to validate it in isolation.

Example:
```csharp
using MyProject.Contracts.Interfaces;
using MyProject.Domain.Interfaces.Repositories;
using MyProject.Domain.Validators;
using NSubstitute;
using Xunit;
using ValidationResult = SebastianGuzmanMorla.Validator.ValidationResult;

namespace MyProject.Domain.Tests.Validators;

public class DomainValidatorsTests : ValidatorTestBase
{
    private readonly ClientIdValidator _clientIdValidator;
    private readonly IClientRepository _clientRepository;

    public DomainValidatorsTests()
    {
        _clientIdValidator = new ClientIdValidator();
        _clientRepository = Substitute.For<IClientRepository>();

        // Register repository mock inside ServiceProvider
        ServiceProvider.GetService(typeof(IClientRepository)).Returns(_clientRepository);
    }

    [Fact]
    public async Task ClientIdValidator_WhenClientExists_ShouldBeValid()
    {
        Guid clientId = Guid.NewGuid();
        var entity = new TestClientIdValidation { ClientId = clientId };
        _clientRepository.Any(clientId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        ValidationResult result = await _clientIdValidator.Validate(entity, ServiceProvider);

        Assert.True(result.IsValid);
    }

    private class TestClientIdValidation : IClientIdValidation
    {
        public Guid ClientId { get; set; }
    }
}
```

---

## 11. HTTP Routing & Endpoint Mapping Pattern (`MapRequest`)

To keep controllers and minimal API route mappings extremely clean and declarative, the solution centralizes routing definitions inside request DTO contracts.

### A. Centralizing Route and Method in the Request
Every CQRS request contract must declare its own route and HTTP method:

```csharp
using SebastianGuzmanMorla.DDD.Domain.Attributes;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace MyProject.Contracts.Messaging.Customers;

public class GetCustomerRequest : Request<GetCustomerResponse>
{
    // Define endpoint constants directly on the contract request object
    public const string Route = "/Customers/{id:guid}";
    public const RequestMethod Method = RequestMethod.Get;

    public Guid Id { get; set; }
}
```

### B. Standard Endpoint Mapping (`MapRequest`)
Use the `MapRequest<TRequest, TResponse>` extension method to map request contracts automatically to minimal API endpoints:

```csharp
using MyProject.Contracts.Messaging.Customers;
using Microsoft.AspNetCore.Routing;
using SebastianGuzmanMorla.DDD.Extensions;

namespace MyProject.Web.Endpoints;

public static class CustomerEndpoints
{
    public static void MapCustomerEndpoints(this RouteGroupBuilder group)
    {
        // Automatically maps the route and method declared on the request contract, 
        // binds the inputs, invokes the handler, and returns a formatted JSON Response
        group.MapRequest<GetCustomerRequest, GetCustomerResponse>(
                GetCustomerRequest.Method,
                GetCustomerRequest.Route,
                "Customers")
            .RequireAuthorization("MyPolicyName");
    }
}
```

* **Behavior**:
  * For `GET` or `DELETE` methods, parameters are bound using `[AsParameters]` from the query or route values.
  * For `POST`, `PUT`, or `PATCH` methods, the request DTO is bound using `[FromBody]` from the HTTP request body.
  * Successful responses (inheriting from `Response`) with `HttpStatusCode.OK` are returned as `Results.Json`.
  * Responses inheriting from `ResponseFile` (like `ResponseFileByte` or `ResponseFilePath`) are returned as download files.
  * Validation errors (400), not found (404), and internal errors (500) are mapped automatically.

### C. Advanced Request Mapping with Custom Binders
If a request contract requires custom source mapping (e.g. reading credentials from Authorization headers, cookies, or custom query binders), specify the target binder response model using `MapRequest<TRequest, TResponse, TBinderResponse>`:

```csharp
group.MapRequest<CustomTokenRequest, TokenResponse, ErrorResponse>(
    CustomTokenRequest.Method,
    "/oauth/token",
    "/oauth/token", // Prefixed full route
    "OAuth"
);
```

---

## 12. Policy-Based Authorization via Smart Enums

To ensure authorization scopes and roles stay synchronized, configure ASP.NET Core Policy-Based Authorization policies using Smart Enum values.

### A. Register the Handler
Register `SmartEnumRequirementHandler<TFlags, TEnum, TValue>` in the DI container to evaluate authorization rules:

```csharp
// Register the authorization requirement handler for Scopes (Flags enum of Scope)
builder.Services.AddSingleton<IAuthorizationHandler, SmartEnumRequirementHandler<Scopes, Scope, string>>();
```

### B. Define Policies using Smart Enums
Configure policies during authorization setup in `Program.cs`:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("scope:customers:read", policy =>
        policy
            .AddAuthenticationSchemes("Bearer")
            .RequireAuthenticatedUser()
            // Require that the scope claim contains the custom smart enum flag for ReadCustomers
            .AddRequirements(new SmartEnumRequirement<Scopes, Scope, string>(Scope.ReadCustomers))
    );
});
```

---

## 13. Global Exception Handling Middleware

To ensure AOT-compatible serialization of errors and unified system tracking, register `ExceptionHandlerMiddleware<TContext>` in the HTTP pipeline.

### A. How it Works
The middleware traps all pipeline exceptions:
1. **`TaskCanceledException`**: Maps to HTTP status `499 Client Closed Request`.
2. **`BadHttpRequestException`**: Maps to HTTP status `400 BadRequest` returning a standard JSON response with the error details.
3. **Generic `Exception`**: 
   * Maps to HTTP status `500 InternalServerError`.
   * Automatically creates a new `Log` entity with `LogType.Error` and a generated UUID Version 7.
   * Persists the log record to the database (using `TContext`).
   * Returns a JSON payload containing the `LogId` of the saved log so developers can trace the exact stack trace in the database.

### B. Registering the Middleware
Add it to the HTTP request pipeline right after mapping health checks:

```csharp
app.UseMiddleware<ExceptionHandlerMiddleware<MyDbContext>>();
```

---

## 14. Cached Health Checks

To optimize performance and protect critical systems from high-frequency status requests, health check results are cached in Redis.

### A. Background Runner (`CachedHealthCheckService`)
A background host service runs the system checks periodically, updates the cached status in Redis under a distributed lock, and formats the output into a standardized `HealthCheckReportModel`.

Register the caching settings and service in `Program.cs`:

```csharp
builder.Services.AddOptions<CachedHealthCheckOptions>()
    .Configure<IOptions<HealthCheckSettings>>((options, settings) =>
    {
        options.RedisKey = "MyProject:health";
        options.RedisLockKey = "MyProject:locks:health";
        options.CacheIntervalSeconds = settings.Value.CacheIntervalSeconds;
    });

builder.Services.AddSingleton<CachedHealthCheckService>();
builder.Services.AddHostedService(serviceProvider =>
    serviceProvider.GetRequiredService<CachedHealthCheckService>());
```

### B. Map Cached Health Checks Endpoint
Expose the endpoint in `Program.cs`:

```csharp
app.MapCachedHealthChecks("/health");
```

* **HTTP Status Code Mapping**:
  * `"Healthy"` -> `200 OK`
  * `"Degraded"` -> `429 TooManyRequests`
  * `"Unhealthy"` / Other -> `503 ServiceUnavailable`

---

## 15. Cryptographic Secret Hashing (`SecretHasher` / `ISecretHash`)

For secure password and token storage, utilize the transversal cryptography utilities.

### A. SecretHasher Utility
`SecretHasher` implements salted key derivation using PBKDF2 (SHA256 with 100,000 iterations):

* **Hash a secret**:
  ```csharp
  string hashedPassword = SecretHasher.Hash("myPlainPassword");
  ```
* **Verify a secret**:
  ```csharp
  bool isValid = SecretHasher.Verify("myPlainPassword", hashedPassword);
  ```

### B. Entity Integration (`ISecretHash`)
If a domain entity implements the `ISecretHash` interface, you can verify plain credentials directly against its properties using the `ValidateSecret` extension method:

```csharp
using SebastianGuzmanMorla.DDD.Domain.Interfaces;

namespace MyProject.Domain.Entities;

public class Client : Entity, ISecretHash
{
    public string? SecretHash { get; set; }
}

// In Application layer / handlers:
bool isMatch = clientEntity.ValidateSecret("plainSecretText");
```

---

## 16. Web Application Bootstrapping (`Program.cs`)

To wire up all transversal dependencies, source generators, and middlewares correctly, follow this standard bootstrapper structure in the entry point `Program.cs`:

```csharp
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MyProject.Contracts;
using MyProject.Domain;
using MyProject.Infrastructure;
using MyProject.Application;
using SebastianGuzmanMorla.DDD.Infrastructure.Middleware;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// 1. Insert source-generated serialization contexts to support reflection-free JSON
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, ContractsJsonSerializerContext.Default);
    o.SerializerOptions.TypeInfoResolverChain.Insert(1, DomainJsonSerializerContext.Default);
});

// 2. Chain autogenerated dependency injection helper extension methods
builder.Services
    .AddLocalization()
    .ConfigureLocalization()      // From Project.Localization
    .ConfigureDomain()            // From Project.Domain (Validator Generator)
    .ConfigureInfrastructure(options =>
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("MyDatabase"))
               .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    })                            // From Project.Infrastructure (Repositories Generator)
    .ConfigureApplication()       // From Project.Application (Handlers Generator)
    .ConfigureBinders();          // From Project.Web (Binders Generator)

WebApplication app = builder.Build();

// 3. Register Global Exception Handling and Middlewares
app.UseMiddleware<ExceptionHandlerMiddleware<MyDbContext>>();

app.UseAuthentication();
app.UseAuthorization();

// 4. Map endpoints
app.MapCustomerEndpoints(); // Or MapApiEndpoints() mapping groups

await app.RunAsync();
```

---

## 17. Audit Logging Architecture

Every bounded context inherits from a project-specific base `RequestHandler` that overrides execution lifecycle hooks to manage audit trails automatically.

### A. Database Schema
Audit records are divided into two entities (persisted to the DB):
- **`LogRequest`**: Captures global request metadata (IP address, user/subject claims, target route, request method, execution timestamp, and the serialized JSON request body with sensitive credentials redacted).
- **`Log`**: Captures granular entity state changes and events logged during the request execution (reference entity Type, ID, change Type, and serialized data).

### B. Implementation Pattern
Ensure your request handlers inherit from the project's base `RequestHandler` (not the library's raw one). This base handler manages transactions and maps logs:

```csharp
using SebastianGuzmanMorla.DDD.Domain.Attributes;
using SebastianGuzmanMorla.DDD.Domain.Entities;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace MyProject.Application.Handlers;

public abstract class RequestHandler<TContext, TRequest, TResponse>(
    IServiceProvider serviceProvider
) : SebastianGuzmanMorla.DDD.Infrastructure.Handlers.RequestHandler<TContext, TRequest, TResponse>(serviceProvider)
    where TContext : DbContext
    where TRequest : Request<TResponse>
    where TResponse : Response, new()
{
    private readonly JsonSerializerOptions _jsonSerializerOptions = serviceProvider.GetRequiredService<JsonSerializerOptions>();
    private readonly ILogRepository _logRepository = serviceProvider.GetRequiredService<ILogRepository>();
    private readonly ILogRequestRepository _logRequestRepository = serviceProvider.GetRequiredService<ILogRequestRepository>();
    private readonly List<INotificationLog> _logs = [];

    protected override async Task OnException(TRequest request, Exception exception, CancellationToken cancellationToken)
    {
        AddLog(LogType.Error, exception.ToString());
        await Task.CompletedTask;
    }

    protected override async Task OnAfterExecute(TRequest request, TResponse response, CancellationToken cancellationToken)
    {
        bool logIgnore = request.GetType().GetCustomAttributes(typeof(LogIgnoreAttribute), true).Length != 0;
        bool logError = _logs.Any(x => x.Type == LogType.Error);

        // Skip logging for query operations marked with [LogIgnore] unless an error was logged
        if (!logError && (_logs.Count == 0 || logIgnore)) return;

        await using (UnitOfWork)
        {
            try
            {
                await UnitOfWork.CreateTransaction(cancellationToken);

                // Serialize the request (sensitive fields will be cleared by source-generated methods)
                LogRequest logRequest = request.ToLogEntity(IdentityDataService, _jsonSerializerOptions);
                await _logRequestRepository.Add(cancellationToken, logRequest);

                // Attach the Request Log Id to the Response
                response.LogId = logRequest.Id;

                // Map and save nested entity/message logs
                List<Log> logEntries = _logs
                    .Select(x => x.ToLogEntity(_jsonSerializerOptions, logRequest.Id))
                    .ToList();

                await _logRepository.Add(cancellationToken, logEntries);
                await UnitOfWork.Commit(cancellationToken);
            }
            catch (Exception ex)
            {
                AddLog(LogType.Error, ex.ToString());
                await UnitOfWork.Rollback(cancellationToken);
            }
        }
    }
}
```

* **RULE**: Mark query requests (which read data and do not modify state) with the `[LogIgnore]` attribute to avoid flooding the database with audit rows.

### C. Request Cloning & Redaction (`ToLogEntity` Extension)
To log requests safely, the system utilizes a custom extension method. It clones the request object first before executing `.ClearSensitiveProperties()`. This ensures that sensitive credentials (like passwords) are redacted from database logs while preventing the active execution thread from losing its inputs:

```csharp
using System.Text.Json;
using SebastianGuzmanMorla.DDD.Domain.Entities;
using SebastianGuzmanMorla.DDD.Domain.Extensions;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

public static class LoggingExtensions
{
    public static LogRequest ToLogEntity(this Request request, IIdentityData identity, JsonSerializerOptions jsonSerializerOptions)
    {
        // Clone the request to prevent mutating the state of the request in the active pipeline
        Request clonedRequest = (Request)request.Clone();
        
        // Redact any property marked with [SensitiveData]
        clonedRequest.ClearSensitiveProperties();

        var contextData = new
        {
            UserId = identity.UserId,
            ClientId = identity.ClientId,
            DeviceId = identity.DeviceId
        };

        return new LogRequest
        {
            Id = Guid.CreateVersion7(),
            Context = JsonSerializer.Serialize(contextData),
            Type = request.GetType().Name,
            Request = JsonSerializer.Serialize(clonedRequest, request.GetType(), jsonSerializerOptions),
            UpdatedAt = DateTime.UtcNow
        };
    }
}
```

---

## 18. UI Validation State Mapping (Razor Pages)

When validation results are returned to Blazor, controllers, or Razor Page models, use validation mapping extension methods to populate the UI view model state automatically.

### C# Extension Method
Implement an extension on ASP.NET Core `PageModel` to convert `SebastianGuzmanMorla.Validator` results into `ModelState` errors:

```csharp
using Microsoft.AspNetCore.Mvc.RazorPages;
using SebastianGuzmanMorla.Validator;

namespace MyProject.Web.Extensions;

public static class PageModelExtensions
{
    // C# Extension Type Syntax
    extension(PageModel pageModel)
    {
        public PageResult AddValidationErrors(ValidationResult result)
        {
            foreach ((string field, List<string> errors) in result.Errors ?? [])
            {
                foreach (string error in errors)
                {
                    // Clean property paths (e.g. removing JsonPath prefixes like "$.")
                    pageModel.ModelState.AddModelError(field.Replace("$.", ""), error);
                }
            }

            return pageModel.Page();
        }
    }
    }
}
```

### B. PageModel Validation Interface Pattern
To reuse validation rules between API endpoints and Razor Pages, declare the binding properties on a shared validation interface, and have the `PageModel` implement the interface. The `PageModel` can then pass `this` directly to the validator:

1. **Define the Interface (`[Project].Contracts/Interfaces`)**:
   ```csharp
   using SebastianGuzmanMorla.Validator.Interfaces;
   
   namespace MyProject.Contracts.Interfaces;
   
   public interface ILoginValidation : IEntityValidation
   {
       string Email { get; }
       string Password { get; }
   }
   ```

2. **Implement the Interface on the PageModel (`PageModel`)**:
   ```csharp
   using Microsoft.AspNetCore.Mvc;
   using Microsoft.AspNetCore.Mvc.RazorPages;
   using SebastianGuzmanMorla.Validator;
   using MyProject.Contracts.Interfaces;
   
   namespace MyProject.Web.Pages;
   
   public class LoginModel(
       IValidator<ILoginValidation> validator,
       IServiceProvider serviceProvider
   ) : PageModel, ILoginValidation
   {
       [BindProperty]
       public string Email { get; set; } = "";
   
       [BindProperty]
       public string Password { get; set; } = "";
   
       public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
       {
           // Validate the PageModel instance itself against the validation interface
           ValidationResult result = await validator.Validate(this, serviceProvider, cancellationToken);
   
           if (!result.IsValid)
           {
               // Add errors to ModelState and refresh Page
               return this.AddValidationErrors(result);
           }
   
           // Proceed with login logic...
           return Redirect("/Home");
       }
   }
   ```

---

## 19. Custom Request Binders (`IRequestBinder`)

For endpoints that parse data from form fields, URL-encoded bodies, query strings, headers, or cookies (such as OAuth / OIDC endpoints), implement a custom request binder.

### A. Implementing the Binder
The binder must implement `IRequestBinder<TRequest, TErrorResponse>`:

```csharp
using Microsoft.AspNetCore.Http;
using SebastianGuzmanMorla.DDD.Interfaces;
using SebastianGuzmanMorla.SmartEnum;

namespace MyProject.Web.Binders;

public class LoginRequestBinder(IHttpContextAccessor httpContextAccessor)
    : IRequestBinder<LoginRequest, ErrorResponse>
{
    public async Task<(LoginRequest?, ErrorResponse?)> BindAsync(CancellationToken cancellationToken = default)
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null) 
            return (null, new ErrorResponse { Message = "HttpContext not found." });

        if (!httpContext.Request.HasFormContentType) 
            return (null, new ErrorResponse { Message = "Invalid content type." });

        IFormCollection form = await httpContext.Request.ReadFormAsync(cancellationToken);

        try
        {
            var request = new LoginRequest
            {
                Username = form["username"],
                Password = form["password"],
                // Safe smart enum parsing
                DeviceType = DeviceType.TryParse(form["device_type"], out DeviceType? devType) ? devType : DeviceType.Web
            };

            return (request, null);
        }
        catch (SmartEnumException ex)
        {
            return (null, new ErrorResponse { Message = $"Invalid enum choice: {ex.Message}" });
        }
        catch (Exception ex)
        {
            return (null, new ErrorResponse { Message = ex.Message });
        }
    }
}
```

### B. Route Configuration
Binders are automatically picked up by the source generator and registered in DI. Use the three-parameter `MapRequest` overload to map them:

```csharp
group.MapRequest<LoginRequest, LoginResponse, ErrorResponse>(
    LoginRequest.Method,
    "/auth",
    "/auth/login", // Prefixed endpoint route
    "Auth"
);
```

---

## 20. Identity & Audit Context Pattern (`IIdentityData`)

To track the security context of callers executing commands/queries, the application uses an identity data contract resolved from user claims.

### A. Define the Interface
Create a lightweight interface to define the standard audit context properties in `[Project].Contracts`:

```csharp
namespace MyProject.Contracts.Interfaces;

public interface IIdentityData
{
    Guid? UserId { get; }
    Guid? ClientId { get; }
    Guid? DeviceId { get; }
}
```

### B. Injection and Usage
The custom `RequestHandler` base class resolves the current `IIdentityData` implementation from the DI container during `OnAfterExecute` to populate the `LogRequest` actor fields, ensuring every database operation is linked to the user/device executing it.

---

## Key Guidelines

1. **No DbContext in Handlers**: Handlers should interact with the database via Repositories to maintain layer separation.
2. **Unit of Work**: Use `UnitOfWork` for transactional boundaries in state-modifying requests.
3. **Dependency Injection**: Dependencies inside Handlers must be resolved through the constructor's `IServiceProvider` parameter using `serviceProvider.GetRequiredService<T>()`. Do not use constructor injection for other dependencies directly in the handler constructor to preserve clean inheritance.
4. **Keep Domain Models Rich**: Do not make domain models simple DTO containers (anemic models). Define behavioral methods inside the entities to mutate state and assert business invariants.
5. **Entity Logging Rules**:
   * For **Create or Update** operations, always use `LogType.Put` (e.g., `AddEntityLog(LogType.Put, entity)`). There is no "Create" or "Update" log type.
   * For **Delete** operations, always use `LogType.Delete`.
   * For **General information / tracking**, use `LogType.Information`.
   * For **Non-critical issues / alerts**, use `LogType.Warning`.
   * For **Exceptions / Failures**, always use `LogType.Error`.
