# AI Agent Integration Guide (`AGENTS.md`)

This document provides system prompt context, syntax, and guidelines for AI coding assistants (such as Cursor, Copilot, Antigravity, etc.) to correctly utilize, configure, and author domain entities, repositories, and request handlers using the `SebastianGuzmanMorla.DDD` libraries in C# .NET 10.0+ projects.

---

## 1. Project Architecture & Structure

The codebase is split into four main components:
1. **`SebastianGuzmanMorla.DDD.Domain`**: Contains base entities (`Entity`), core interfaces (`IRepository`, `IRequestHandler`, `IUnitOfWork`, `INotification`), attributes (`[SensitiveData]`, `[LogIgnore]`), and message definitions (`Request`, `Response`).
2. **`SebastianGuzmanMorla.DDD`**: Implements base Minimal API mapping (`MapRequest`), smart enum authorization requirements, and OpenAPI transformers. It is clean of database-specific dependencies (such as EF Core or Redis).
3. **`SebastianGuzmanMorla.DDD.Infrastructure`**: Implements EF Core repositories, caching decorators (`CachedRepository`), unit of work (`UnitOfWork`), base handlers (`RequestHandler`, `RequestPageHandler`), Redis cached health check services, and exception middleware.
4. **`SebastianGuzmanMorla.DDD.Generator`**: Roslyn Source Generators to automate DI registration, sensitive property scrubbing, and EF Core entity type configurations.

### NuGet Installation Quick Reference
Depending on the layer, agents should suggest/add the correct package reference:
- **Domain/Contracts Layer**: `dotnet add package SebastianGuzmanMorla.DDD.Domain --version 1.0.3`
- **Application/Web API Layer**: `dotnet add package SebastianGuzmanMorla.DDD --version 1.0.3`
- **Infrastructure/Persistence Layer**: `dotnet add package SebastianGuzmanMorla.DDD.Infrastructure --version 1.0.3`

---

## 2. Entities & Auditing

All domain models must inherit from `Entity` (from `SebastianGuzmanMorla.DDD.Domain.Entities`).
- **Identifier**: `Id` is a `Guid` initialized automatically using **UUID v7** (`Guid.CreateVersion7()`). Do NOT generate `Guid.NewGuid()` manually unless specifically required.
- **Audit Properties**: `CreatedAt` (UTC, init-only), `UpdatedAt` (UTC, mutable), and `DeletedAt` (nullable UTC, for soft deletion).

```csharp
using SebastianGuzmanMorla.DDD.Domain.Entities;

public class Customer : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
```

---

## 3. Repositories & Unit of Work

### Repository Definition
To define a repository for an entity:
1. Declare an interface inheriting from `IRepository<TEntity>`:
   ```csharp
   public interface ICustomerRepository : IRepository<Customer>
   {
       Task<Customer?> GetByEmail(string email, CancellationToken cancellationToken = default);
   }
   ```
2. Implement it inheriting from `Repository<TContext, TEntity>`:
   ```csharp
   using SebastianGuzmanMorla.DDD.Infrastructure.Repositories;

   public class CustomerRepository(IServiceProvider serviceProvider) 
       : Repository<MyDbContext, Customer>(serviceProvider), ICustomerRepository
   {
       public async Task<Customer?> GetByEmail(string email, CancellationToken cancellationToken = default)
       {
           return await Queryable.FirstOrDefaultAsync(x => x.Email == email, cancellationToken);
       }
   }
   ```

### Redis Cached Repositories (`CachedRepository`)
If an entity requires automatic Redis caching decoration, implement the repository inheriting from `CachedRepository<TContext, TEntity>` instead of `Repository<TContext, TEntity>`:

- **Automatic Cache Management**:
  - `FirstOrDefault(id)` checks cache first. If cache is empty, it queries the database and populates the cache.
  - `Add` registers a post-commit action to serialize and save the item in cache.
  - `Update`, `Upsert`, `SoftDelete`, and `HardDelete` register post-commit actions to invalidate cache keys.
- **Required Overrides**:
  - `CacheKeyPrefix`: The string prefix used for Redis keys (e.g. `"Customer"`).
  - `JsonTypeInfo`: A `JsonTypeInfo<TEntity>` instance for standard metadata-driven JSON serialization.
  - `CacheExpiry` (optional): Defaults to 10 minutes.

```csharp
using SebastianGuzmanMorla.DDD.Infrastructure.Repositories;
using System.Text.Json.Serialization.Metadata;

public class CustomerRepository(IServiceProvider serviceProvider) 
    : CachedRepository<MyDbContext, Customer>(serviceProvider), ICustomerRepository
{
    protected override string CacheKeyPrefix => "Customer";
    
    protected override TimeSpan CacheExpiry => TimeSpan.FromMinutes(15);
    
    protected override JsonTypeInfo<Customer> JsonTypeInfo => MyJsonSerializerContext.Default.Customer;
}
```

### Querying Rules
- **No-Tracking by Default**: `Queryable` in the repository base is configured with `.AsNoTracking()` and excludes soft-deleted items (`Where(x => x.DeletedAt == null)`).
- **Direct DB Access**: If tracking is needed or deleted items must be queried, use `DbSet` directly instead of `Queryable`.

### Transaction & Save Changes Behavior
- If `UnitOfWork.TransactionEnabled` is **false**: Every database-modifying operation (`Add`, `Update`, `Upsert`, `SoftDelete`, `HardDelete`) automatically calls `SaveChangesAsync()` and detaches entities immediately.
- If `UnitOfWork.TransactionEnabled` is **true**: DB changes are not auto-saved on each repository call. You must explicitly call `UnitOfWork.Commit()` to persist changes.

### Dependency Injection Registration
Do NOT register repositories manually in `Program.cs`. Instead, declare a partial class `ConfigureRepositoryServices` in your infrastructure project:
```csharp
public static partial class ConfigureRepositoryServices
{
    public static IServiceCollection ConfigureInfrastructure(this IServiceCollection services)
    {
        // Roslyn generator automatically outputs the implementation of this method
        ConfigureGenerated(services);
        return services;
    }
}
```

---

## 4. Request / Response Messaging & Handlers

All API operations and use cases should be structured as requests and request handlers.

### 1. Declaring Requests & Responses
- Request class inherits from `Request<TResponse>`.
- Response class inherits from `Response` (optionally `Response<TData>`).
- If a Request contains sensitive properties (like passwords, keys, tokens), mark them with the `[SensitiveData]` attribute and declare the class as `partial` to let the generator write the `ClearSensitiveProperties()` body.

```csharp
using SebastianGuzmanMorla.DDD.Domain.Attributes;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

public partial class AuthenticateUserRequest : Request<Response<string>>
{
    public required string Username { get; set; }

    [SensitiveData]
    public required string Password { get; set; }
}
```

### Exclusion from Logging (`[LogIgnore]`)
If a request is high-frequency or does not need audit logging in the database, decorate it with the `[LogIgnore]` attribute (from `SebastianGuzmanMorla.DDD.Domain.Attributes`):

```csharp
using SebastianGuzmanMorla.DDD.Domain.Attributes;

[LogIgnore]
public partial class HeartbeatRequest : Request<Response>
{
}
```

### 2. Request Handlers
Implement handlers inheriting from `RequestHandler<TContext, TRequest, TResponse>`:
- The base handler wraps `Execute` inside a database transaction managed by `IUnitOfWork<TContext>`.
- If an `IValidator<TRequest>` is registered in the dependency injection container, the handler automatically performs validation before invoking `Execute`. If validation fails, it returns a `400 BadRequest` response with errors.

```csharp
using SebastianGuzmanMorla.DDD.Infrastructure.Handlers;

public class AuthenticateUserHandler(IServiceProvider serviceProvider)
    : RequestHandler<MyDbContext, AuthenticateUserRequest, Response<string>>(serviceProvider)
{
    protected override async Task<Response<string>> Execute(
        AuthenticateUserRequest request, 
        CancellationToken cancellationToken = default)
    {
        // Your logic here...
        return new Response<string> { Data = "jwt-token-here" };
    }
}
```

### 3. Handler Dependency Injection Registration
Declare a partial class `ConfigureHandlerServices` in your application or handler project:
```csharp
public static partial class ConfigureHandlerServices
{
    public static IServiceCollection ConfigureApplication(this IServiceCollection services)
    {
        // Roslyn source generator automatically registers all IRequestHandler and INotificationHandler
        ConfigureGenerated(services);
        return services;
    }
}
```

---

## 5. Hashing & Security (ISecretHash & SecretHasher)

For storing and validating sensitive secrets/passwords (such as user credentials), the library provides standard interfaces and hashing utilities:

### The `ISecretHash` Interface
Any domain model or entity that stores a hashed credential (e.g., `User`, `Client`) should implement the `ISecretHash` interface (from `SebastianGuzmanMorla.DDD.Domain.Interfaces`):

```csharp
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.DDD.Domain.Entities;

public class User : Entity, ISecretHash
{
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? SecretHash { get; set; }
}
```

### Password/Secret Hashing
When creating or updating credentials, use the `SecretHasher` utility class (`SebastianGuzmanMorla.DDD`) which implements PBKDF2 with SHA256:

```csharp
using SebastianGuzmanMorla.DDD;

user.SecretHash = SecretHasher.Hash(plainTextPassword);
```

### Password/Secret Verification
To verify a password against the stored hash, import `SebastianGuzmanMorla.DDD.Extensions` and call `ValidateSecret(password)` on any object implementing `ISecretHash`:

```csharp
using SebastianGuzmanMorla.DDD.Extensions;

if (user.ValidateSecret(password))
{
    // Success
}
```

---

## 6. Rule Localization (`IRuleLocalization`)

For validation message localization in validators, the library provides the `IRuleLocalization` interface under `SebastianGuzmanMorla.DDD.Domain.Interfaces`. This interface standardizes common validation error messages:

- `NotNull(string label)`
- `NotEmpty(string label)`
- `Maximum(string label, int max)`
- `AlreadyExists(string label)`
- `Immutable(string label)`
- `MaximumLength(string label, int length)`
- `MinimumLength(string label, int length)`
- `NotExists(string label)`
- `NotValid(string label)`

Example usage inside a validator:
```csharp
RuleFor(x => x.Name)
    .NotNull((x, _) => x.GetRequiredService<IRuleLocalization>().NotNull("Name"));
```

---

## 7. Entity Framework Core Configuration Mapping

Entity configurations implementing `IEntityTypeConfiguration<TEntity>` are automatically tracked.
The source generator generates `ModelBuilderGeneratedExtensions` under the `Identity.Infrastructure` namespace. In your DbContext, apply them automatically:

```csharp
using Identity.Infrastructure;

public class MyDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Applies all configuration classes generated by EntityTypeConfigurationGenerator
        modelBuilder.ApplyGeneratedConfigurations();
    }
}
```

---

## 8. ASP.NET Core Integration & Features

The library contains ASP.NET Core integrations that simplify Web App structure, endpoints, error handling, health checks, and authorization.

### 1. Minimal API Request Mapping (`MapRequest`)
Use `MapRequest<TRequest, TResponse>` (from `SebastianGuzmanMorla.DDD.Extensions`) to route incoming requests straight to their CQS Handlers. This eliminates boilerplate controllers or manual resolution.

- `GET` or `DELETE` requests bind using `[AsParameters] TRequest request`.
- `POST`, `PUT`, or `PATCH` requests bind using `[FromBody] TRequest request`.

#### Custom Request Binding (`IRequestBinder`)
If a request requires custom binding logic (e.g. retrieving request values from headers, cookies, query parameters, route data, or multi-part form data), implement `IRequestBinder<TRequest, TErrorResponse>`:

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

The source generator automatically registers `IRequestBinder<TRequest, TErrorResponse>` implementations in the dependency injection container. Then, map it using the three-parameter overload:

```csharp
group.MapRequest<MyCustomRequest, MyResponse, Response>(
    RequestMethod.Post,
    "/prefix",
    "/route",
    "CustomTag"
);
```

#### File Responses (`ResponseFile`)
For endpoints that return files, requests should return a `ResponseFile` (such as `ResponseFileByte` or `ResponseFilePath`). `MapRequest` automatically maps these to `Results.File`:

```csharp
public class GetReportRequest : Request<ResponseFileByte> { ... }
```

```csharp
using SebastianGuzmanMorla.DDD.Extensions;

public static class ProductsEndpoints
{
    public static void MapProductEndpoints(this RouteGroupBuilder group)
    {
        // Automatically maps the route and executes the handler via DI
        group.MapRequest<CreateProductRequest, CreateProductResponse>(
            CreateProductRequest.Method,
            CreateProductRequest.Route,
            "ProductsTag"
        );
    }
}
```

### 2. Global Exception Handling Middleware (`ExceptionHandlerMiddleware`)
Configure the generic `ExceptionHandlerMiddleware<TContext>` (from `SebastianGuzmanMorla.DDD.Middleware`) in your request pipeline. It captures exceptions globally and does the following:

- **General Exceptions (500)**: Logs the stack trace, writes an EF Core `Log` entity to the database using `TContext` with a UUID v7, and returns a JSON payload containing the logged `LogId` and a generic message.
- **BadHttpRequestException (400)**: Logs the error and returns a 400 Bad Request status with the exception message.
- **TaskCanceledException (499)**: Returns status 499 (Client Closed Request).

```csharp
// Program.cs
using SebastianGuzmanMorla.DDD.Infrastructure.Middleware;

app.UseMiddleware<ExceptionHandlerMiddleware<MyDbContext>>();
```

### 3. Cached Health Checks (`MapCachedHealthChecks`)
To prevent hammering databases and external APIs on every health check poll, configure the distributed caching health checks:

1. **Configure Options**:
   ```csharp
   builder.Services.AddOptions<SebastianGuzmanMorla.DDD.Domain.Options.CachedHealthCheckOptions>()
       .Configure(options =>
       {
           options.RedisKey = "AppName:health";
           options.RedisLockKey = "AppName:locks:health";
           options.CacheIntervalSeconds = 30;
       });
   ```

2. **Register the Service & Map the Endpoint**:
   ```csharp
   using SebastianGuzmanMorla.DDD.Infrastructure.Extensions;
   using SebastianGuzmanMorla.DDD.Infrastructure.Services;

   builder.Services.AddSingleton<CachedHealthCheckService>();
   builder.Services.AddHostedService(sp => sp.GetRequiredService<CachedHealthCheckService>());

   app.MapCachedHealthChecks("/health");
   ```

### 4. Claims & Smart Enum Authorization (`SmartEnumRequirement`)
Enforce endpoint-level authorization checking claims against flags defined using the `SebastianGuzmanMorla.SmartEnum` library:

1. **Register the handler**:
   ```csharp
   builder.Services.AddSingleton<IAuthorizationHandler, SmartEnumRequirementHandler<Scopes, Scope, string>>();
   ```
2. **Add policy requirement**:
   ```csharp
   builder.Services.AddAuthorization(options =>
   {
       options.AddPolicy("AdminPolicy", policy =>
           policy
               .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
               .RequireAuthenticatedUser()
               .AddRequirements(new SmartEnumRequirement<Scopes, Scope, string>(Scope.Administrator))
       );
   });
   ```

### 5. OpenAPI/Swagger Document Transformers with Smart Enums (`ParametersTransformer`)
When utilizing ASP.NET Core OpenAPI (`Microsoft.AspNetCore.OpenApi`), register the `ParametersTransformer<TRequest>` (from `SebastianGuzmanMorla.DDD.Transformers`) to properly document request parameters in Swagger/Scalar.

- It parses `Guid` parameters correctly.
- It automatically maps `SmartEnum` and `SmartEnumFlags` properties, listing allowed enum keys, Regex pattern matching descriptions, and examples in the Swagger/Scalar documentation UI.

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

## 9. Summary of Design Patterns & Conventions

| Target | Convention | Source Generator Effect |
| :--- | :--- | :--- |
| **Domain Entities** | Inherit `Entity`, use Guid version 7, partial if they require custom EF mappings. | None |
| **Repositories** | Inherit interface `IRepository<T>`, implement class `Repository<TContext, T>`. | Registered via `ConfigureRepositoryServices.ConfigureGenerated(services)`. |
| **Cached Repositories** | Implement class `CachedRepository<TContext, T>`. | Registered via `ConfigureRepositoryServices.ConfigureGenerated(services)`. |
| **Requests** | Inherit `Request<TResponse>`, mark class `partial`. | Scrubs `[SensitiveData]` fields in generated `ClearSensitiveProperties()`. |
| **Request Handlers**| Inherit `RequestHandler<TContext, TReq, TRes>`. | Registered via `ConfigureHandlerServices.ConfigureGenerated(services)`. |
| **Secrets / Hashing** | Implement `ISecretHash`, use `SecretHasher.Hash(...)` and `entity.ValidateSecret(...)`. | None |
| **EF Configurations**| Implement `IEntityTypeConfiguration<T>`. | Hooked via `modelBuilder.ApplyGeneratedConfigurations()`. |
| **Notifications** | Implement `INotification`, handlers implement `INotificationHandler<T>`. | Registered as singletons automatically. |
| **Minimal API Routes** | Map using `group.MapRequest<TReq, TRes>(...)`. | None |
| **Custom Request Binders** | Implement `IRequestBinder<TReq, TErr>`. | Registered via `ConfigureHandlerServices.ConfigureGenerated(services)`. |
| **Global Exception Middleware** | Add `app.UseMiddleware<ExceptionHandlerMiddleware<TContext>>()`. | None |
| **Cached Health Checks** | Setup `CachedHealthCheckService` and call `app.MapCachedHealthChecks()`. | None |
| **Smart Enum Authorization** | Use `new SmartEnumRequirement<TFlags, TEnum, TVal>(value)`. | None |
| **OpenAPI Transformers** | Add `new ParametersTransformer<TReq>(...)` to options. | None |
