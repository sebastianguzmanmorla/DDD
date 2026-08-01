# SebastianGuzmanMorla.DDD

A base library for implementing **Domain-Driven Design (DDD)** in .NET 9.0 and .NET 10.0+ that integrates the Repository pattern, Unit of Work, CQS Request/Response messaging, UTC DateTime converters, and a set of Roslyn **Source Generators** to automate Dependency Injection registration for repositories, handlers, entity configurations, and data scrubbing.

## Features

- **Robust Base Entity**: `Entity` abstract class featuring auto-generated **UUID v7** identifiers (`Guid.CreateVersion7()`), temporal audit tracking (`CreatedAt`, `UpdatedAt`), and native Soft Delete support (`DeletedAt`).
- **Repository & Unit of Work Patterns**:
  - Generic, extensible implementation on top of **Entity Framework Core**.
  - Transactional support and automatic or controlled saving (`UnitOfWork`).
  - Built-in repository mutation methods: `Add`, `Update`, `SoftDelete`, `Upsert`, and `HardDelete`.
  - Automatic cache decoration via Redis (`CachedRepository`).
- **UTC DateTime Value Converters**:
  - Native EF Core `UtcDateTimeConverter` and `NullableUtcDateTimeConverter` in `SebastianGuzmanMorla.DDD.Infrastructure.Converters` to enforce UTC Kind compatibility with PostgreSQL.
- **CQS Messaging Architecture**:
  - Standard request and response structures: `Request<TResponse>` and `Response<TData>`.
  - Processing base classes `RequestHandler` and `RequestPageHandler` integrated with the validation engine `SebastianGuzmanMorla.Validator`.
  - Integration for domain notifications (`INotification`, `INotificationHandler`) and post-commit hook execution.
- **Roslyn Source Generators**:
  - **`ConfigureRepositoryServicesGenerator`**: Automatically registers all repositories implementing `IRepository<TEntity>` into the DI container.
  - **`ConfigureHandlerServicesGenerator`**: Automatically registers all request handlers (`IRequestHandler<TRequest, TResponse>`), notification handlers (`INotificationHandler<TNotification>`), and custom request binders (`IRequestBinder<TRequest, TErrorResponse>`).
  - **`ClearSensitivePropertiesGenerator`**: Generates implementation to automatically scrub properties marked with `[SensitiveData]` from `Request` objects using `ClearSensitiveProperties()`.
  - **`EntityTypeConfigurationGenerator`**: Generates extension methods to automatically apply all `IEntityTypeConfiguration<T>` implementations to the EF Core `ModelBuilder`.
- **AI Agent Skills (Progressive Disclosure)**:
  - Packaged AI Coding Assistant Skills (`SKILL.md` and 20 `.skills/` sub-skill modules) included in NuGet packages to instruct agents (Antigravity, Gemini, Copilot, Claude) on generating 100% compliant DDD code.

---

The library is split into three NuGet packages according to responsibilities and dependencies:

1. **`SebastianGuzmanMorla.DDD.Domain`**: Contains core domain entities (`Entity`), base messaging contracts (`Request`, `Response`), abstract interfaces (`IRepository`, `IUnitOfWork`), and core attributes. It is fully independent of ASP.NET Core and external databases.
2. **`SebastianGuzmanMorla.DDD`**: Web and API integration components for ASP.NET Core (e.g. Minimal API `MapRequest` mapping, OpenAPI transformers, Smart Enum authorization requirements). No database or Redis dependencies.
3. **`SebastianGuzmanMorla.DDD.Infrastructure`**: Persistence layer implementing EF Core repositories, Redis cached repositories (`CachedRepository`), transactional Unit of Work, UTC value converters (`UtcDateTimeConverter`, `NullableUtcDateTimeConverter`), exception middleware, and cached health check background services.

---

## Installation

Add the corresponding NuGet package to your project layer:

For the Contract/Domain layer (clean domain models and interfaces):
```bash
dotnet add package SebastianGuzmanMorla.DDD.Domain --version 1.0.5
```

For the Application/Web API layer (requires Minimal API, OpenAPI, or endpoint mappings):
```bash
dotnet add package SebastianGuzmanMorla.DDD --version 1.0.5
```

For the Infrastructure/Persistence layer (requires EF Core, Redis, generic repositories, or transactional handlers):
```bash
dotnet add package SebastianGuzmanMorla.DDD.Infrastructure --version 1.0.5
```

---

## Usage and Components

### 1. Entities and Auditing

Define your entities by inheriting from the `Entity` base class. The `Id` property is automatically initialized using **UUID v7** (via `Guid.CreateVersion7()`), which ensures proper temporal database ordering and excellent index performance:

```csharp
using SebastianGuzmanMorla.DDD.Domain.Entities;

public class Product : Entity
{
    public required string Name { get; set; }
    public decimal Price { get; set; }
}
```

### 2. Repositories and Unit of Work

Define your repository interface by inheriting from `IRepository<TEntity>`:

```csharp
using SebastianGuzmanMorla.DDD.Domain.Interfaces;

public interface IProductRepository : IRepository<Product>
{
    Task<List<Product>> GetExpensiveProducts(decimal minPrice);
}
```

Implement the repository by inheriting from `Repository<TContext, TEntity>`:

```csharp
using SebastianGuzmanMorla.DDD.Infrastructure.Repositories;

public class ProductRepository(IServiceProvider serviceProvider) 
    : Repository<MyDbContext, Product>(serviceProvider), IProductRepository
{
    public async Task<List<Product>> GetExpensiveProducts(decimal minPrice)
    {
        // Queryable automatically applies .AsNoTracking() and .Where(p => p.DeletedAt == null)
        return await Queryable
            .Where(p => p.Price >= minPrice)
            .ToListAsync();
    }
}
```

#### Repository Mutation Methods
All derived repositories inherit built-in mutation methods:
- `await repository.Add(cancellationToken, product);` (inserts entity and sets `UpdatedAt = DateTime.UtcNow`)
- `await repository.Update(cancellationToken, product);` (persists changes and sets `UpdatedAt = DateTime.UtcNow`)
- `await repository.SoftDelete(cancellationToken, product);` (soft deletes entity and sets `DeletedAt = DateTime.UtcNow`)
- `await repository.Upsert(cancellationToken, product);` (bulk PostgreSQL upsert via `FlexLabs.EntityFrameworkCore.Upsert`)
- `await repository.HardDelete(cancellationToken, product);` (bulk SQL delete execution by ID)

#### Redis Cached Repository (`CachedRepository`)
To enable automatic caching on Redis for your entity repository, inherit from `CachedRepository<TContext, TEntity>` instead of `Repository`:

- Automatically queries Redis before falling back to the database.
- Registers post-commit actions to invalidate or refresh cache entries during write operations (`Update`, `Upsert`, `SoftDelete`, `HardDelete`).
- Requires defining the key prefix (`CacheKeyPrefix`), cache expiration (`CacheExpiry`), and System.Text.Json metadata context (`JsonTypeInfo`):

```csharp
using SebastianGuzmanMorla.DDD.Infrastructure.Repositories;
using System.Text.Json.Serialization.Metadata;

public class ProductRepository(IServiceProvider serviceProvider) 
    : CachedRepository<MyDbContext, Product>(serviceProvider), IProductRepository
{
    protected override string CacheKeyPrefix => "Product";
    
    protected override TimeSpan CacheExpiry => TimeSpan.FromMinutes(15);
    
    // Provide compilation metadata for System.Text.Json source generation
    protected override JsonTypeInfo<Product> JsonTypeInfo => MyJsonSerializerContext.Default.Product;

    public async Task<List<Product>> GetExpensiveProducts(decimal minPrice)
    {
        return await Queryable
            .Where(p => p.Price >= minPrice)
            .ToListAsync();
    }
}
```

#### Automatic Repository Registration
Declare the following partial class in your infrastructure layer, and the Roslyn Source Generator will output the registration logic for all repositories implementing `IRepository<TEntity>`:

```csharp
namespace MyProject.Infrastructure;

public static partial class ConfigureRepositoryServices
{
    public static IServiceCollection ConfigureInfrastructure(this IServiceCollection services)
    {
        // This generated method automatically registers all repositories
        ConfigureGenerated(services);
        return services;
    }
}
```

#### UTC DateTime Value Converters
Register built-in UTC converters in your `DbContext.ConfigureConventions` method to automatically ensure DateTime properties use `DateTimeKind.Utc`:

```csharp
using Microsoft.EntityFrameworkCore;
using SebastianGuzmanMorla.DDD.Infrastructure.Converters;

public class MyDbContext(DbContextOptions<MyDbContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>();

        configurationBuilder.Properties<DateTime?>()
            .HaveConversion<NullableUtcDateTimeConverter>();
    }
}
```

### 3. Messaging (Requests, Responses, and Handlers)

Define your CQS Request and Response structures by inheriting from the base classes:

```csharp
using SebastianGuzmanMorla.DDD.Domain.Messaging;

public class CreateProductRequest : Request<CreateProductResponse>
{
    public required string Name { get; set; }
    public decimal Price { get; set; }

    public override void ClearSensitiveProperties()
    {
        // Automatically implemented if the class is partial and contains [SensitiveData]
    }
}

public class CreateProductResponse : Response
{
    public Guid ProductId { get; set; }
}
```

#### Auditing Exclusions (`[LogIgnore]`)
To prevent specific requests (e.g. high-frequency heartbeat ping calls or read-only queries) from producing audit logs in the database, annotate the request with the `[LogIgnore]` attribute:

```csharp
using SebastianGuzmanMorla.DDD.Domain.Attributes;

[LogIgnore]
public class SilentRequest : Request<Response>
{
    // No audit log generated
}
```

Implement the Handler by inheriting from `RequestHandler<TContext, TRequest, TResponse>`:

```csharp
using SebastianGuzmanMorla.DDD.Infrastructure.Handlers;

public class CreateProductHandler(
    IServiceProvider serviceProvider,
    IProductRepository productRepository
) : RequestHandler<MyDbContext, CreateProductRequest, CreateProductResponse>(serviceProvider)
{
    protected override async Task<CreateProductResponse> Execute(
        CreateProductRequest request, 
        CancellationToken cancellationToken = default)
    {
        var product = new Product { Name = request.Name, Price = request.Price };
        
        // Always interact with DB through Repositories, never DbContext directly
        await productRepository.Add(cancellationToken, product);
        
        return new CreateProductResponse { ProductId = product.Id };
    }
}
```

#### Automatic Handler Registration
To automatically wire up all request handlers (`IRequestHandler<,>`) and notification handlers (`INotificationHandler<>`), declare the partial class:

```csharp
namespace MyProject.Application;

public static partial class ConfigureHandlerServices
{
    public static IServiceCollection ConfigureApplication(this IServiceCollection services)
    {
        // Automatically populated at compilation
        ConfigureGenerated(services);
        return services;
    }
}
```

### 4. Security & Hashing (`ISecretHash` and `SecretHasher`)

The library includes built-in structures to handle password/credential hashing using PBKDF2 with SHA-256:

- **`ISecretHash`**: Implement this interface on domain models storing credentials.
- **`SecretHasher`**: Helper class to hash and verify plain-text values.
- **`SecretHashExtensions`**: Extends `ISecretHash` with verification utilities like `ValidateSecret`.

#### Implementing the Entity:
```csharp
using SebastianGuzmanMorla.DDD.Domain.Entities;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;

public class User : Entity, ISecretHash
{
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? SecretHash { get; set; }
}
```

#### Hashing credentials:
```csharp
using SebastianGuzmanMorla.DDD.Domain.Cryptography;

var user = new User
{
    Name = name,
    Email = email,
    SecretHash = SecretHasher.Hash(plainTextPassword)
};

await userRepository.Add(cancellationToken, user);
```

#### Verifying credentials:
```csharp
using SebastianGuzmanMorla.DDD.Domain.Extensions;

User? user = await userRepository.FirstOrDefault(email, cancellationToken);

if (user is null || !user.ValidateSecret(plainTextPassword))
{
    // Invalid credentials
}
```

### 5. Validation Messages Localization (`IRuleLocalization`)

Standardize validation messages within your validators using `IRuleLocalization` (found in `SebastianGuzmanMorla.DDD.Domain.Interfaces`):

- Common error message definitions: `NotNull`, `NotEmpty`, `Maximum`, `AlreadyExists`, `Immutable`, `MaximumLength`, `MinimumLength`, `NotExists`, `NotValid`.

Usage inside a Fluent Validation rule:
```csharp
RuleFor(x => x.Name)
    .NotNull((x, _) => x.GetRequiredService<IRuleLocalization>().NotNull("Name"));
```

### 6. ASP.NET Core Integrations

#### A. Minimal API Routing mapping (`MapRequest`)
Map requests straight to Minimal API endpoints without controllers. It automatically manages binding according to the HTTP verb (`[AsParameters]` for GET/DELETE, `[FromBody]` for POST/PUT/PATCH):

```csharp
using SebastianGuzmanMorla.DDD.Extensions;

public static void MapEndpoints(this RouteGroupBuilder group)
{
    // Maps the route and executes its handler via DI
    group.MapRequest<CreateProductRequest, CreateProductResponse>(
        CreateProductRequest.Method, 
        CreateProductRequest.Route, 
        "ProductsTag"
    );
}
```

##### Custom Request Binding (`IRequestBinder`)
For requests requiring customized binding logic (e.g. reading from headers, cookies, query keys, or form data), implement `IRequestBinder<TRequest, TErrorResponse>`:

```csharp
using SebastianGuzmanMorla.DDD.Interfaces;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

public class CustomRequestBinder(IHttpContextAccessor httpContextAccessor) 
    : IRequestBinder<MyCustomRequest, Response>
{
    public async Task<(MyCustomRequest?, Response?)> BindAsync(CancellationToken cancellationToken = default)
    {
        var context = httpContextAccessor.HttpContext;
        var value = context?.Request.Headers["X-Custom-Header"].ToString();
        
        if (string.IsNullOrEmpty(value))
        {
            return (null, new Response { Status = HttpStatusCode.BadRequest, Message = "Missing custom header" });
        }
        
        return (new MyCustomRequest { HeaderValue = value }, null);
    }
}
```

The binding class is automatically registered by the source generator. Map it using the 3-parameter overload:

```csharp
group.MapRequest<MyCustomRequest, MyResponse, Response>(
    RequestMethod.Post,
    "/prefix",
    "/route",
    "CustomTag"
);
```

##### File Responses (`ResponseFile`)
Endpoints returning files should declare their request output as `ResponseFileByte` or `ResponseFilePath`. `MapRequest` will automatically handle binary responses via `Results.File`.

#### B. Global Exception Handler (`ExceptionHandlerMiddleware`)
A global exception middleware capturing runtime failures and generating appropriate HTTP statuses:

- Captures database transaction issues or general exceptions (returns 500 Internal Server Error) and records an EF Core `Log` entity, outputting a JSON body with a unique `LogId` trace identifier.
- Captures `BadHttpRequestException` (returns 400 Bad Request).
- Captures `TaskCanceledException` (returns 499 Client Closed Request).

```csharp
using SebastianGuzmanMorla.DDD.Infrastructure.Middleware;

// Register in Program.cs
app.UseMiddleware<ExceptionHandlerMiddleware<MyDbContext>>();
```

#### C. Redis-Cached Health Checks (`MapCachedHealthChecks`)
Optimizes system health checks by executing checks in a background hosted service and storing the results in Redis, preventing database spikes under intensive health checks:

1. Configure settings in DI:
```csharp
builder.Services.AddOptions<SebastianGuzmanMorla.DDD.Domain.Options.CachedHealthCheckOptions>()
    .Configure(options =>
    {
        options.RedisKey = "MyProject:health";
        options.RedisLockKey = "MyProject:locks:health";
        options.CacheIntervalSeconds = 30; // Scan interval
    });
```

2. Register the service and endpoint:
```csharp
using SebastianGuzmanMorla.DDD.Infrastructure.Extensions;
using SebastianGuzmanMorla.DDD.Infrastructure.Services;

builder.Services.AddSingleton<CachedHealthCheckService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CachedHealthCheckService>());

app.MapCachedHealthChecks("/health"); // Maps check endpoint (defaults to /health)
```

#### D. Smart Enum Authorization (`SmartEnumRequirement`)
Enforces claim and scope check policies using Smart Enums flags:

1. Add the policy handler:
```csharp
builder.Services.AddSingleton<IAuthorizationHandler, SmartEnumRequirementHandler<MyScopes, MyScope, string>>();
```
2. Define authorization policies:
```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminPolicy", policy =>
        policy
            .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new SmartEnumRequirement<MyScopes, MyScope, string>(MyScope.Administrator))
    );
});
```

#### E. OpenAPI/Swagger Document Transformers (`ParametersTransformer`)
Enables automatic OpenAPI parameter documentation for requests (especially Minimal APIs), parsing `Guid` types correctly and detailing `SmartEnum` or `SmartEnumFlags` properties.

Configure by adding the transformer during Swagger/OpenAPI setup in `Program.cs`:

```csharp
using SebastianGuzmanMorla.DDD.Transformers;

builder.Services.AddOpenApi(options =>
{
    options.AddOperationTransformer(
        new ParametersTransformer<GetProductsRequest>(GetProductsRequest.Route, GetProductsRequest.Method)
    );
});
```

---

## Source Generators Details

### 1. Data Scrubbing (`ClearSensitiveProperties`)
Mark password or sensitive string fields with `[SensitiveData]` and declare the request as `partial`. The source generator automatically outputs the scrubbing logic:

```csharp
using SebastianGuzmanMorla.DDD.Domain.Attributes;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

public partial class LoginRequest : Request<LoginResponse>
{
    public required string Username { get; set; }

    [SensitiveData]
    public required string Password { get; set; }
}
```

### 2. Auto-Configuration of ModelBuilder (EF Core)
The generator tracks classes implementing `IEntityTypeConfiguration<T>` and produces a `ModelBuilderGeneratedExtensions` class inside the `Identity.Infrastructure` namespace:

```csharp
// Inside your DbContext
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyGeneratedConfigurations();
}
```

---

## AI Agent Skills (Progressive Disclosure)

This repository includes a modular AI Agent Skill architecture (`SKILL.md` and 20 `.skills/` sub-skill modules) included directly inside the published NuGet packages. AI coding assistants (Antigravity, Gemini, Copilot, Claude) automatically load these skills to follow strict DDD rules, folder conventions, and repository exclusivity patterns.

---

## Requirements

- **.NET 9.0** or **.NET 10.0+**
- **EF Core 9.0 / 10.0**
- **FlexLabs.EntityFrameworkCore.Upsert** (for bulk upserts)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
