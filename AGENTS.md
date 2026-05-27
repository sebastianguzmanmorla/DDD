# AI Agent Integration Guide (`AGENTS.md`)

This document provides system prompt context, syntax, and guidelines for AI coding assistants (such as Cursor, Copilot, Antigravity, etc.) to correctly utilize, configure, and author domain entities, repositories, and request handlers using the `SebastianGuzmanMorla.DDD` libraries in C# .NET 10.0+ projects.

---

## 1. Project Architecture & Structure

The codebase is split into three main components:
1. **`SebastianGuzmanMorla.DDD.Domain`**: Contains base entities (`Entity`), core interfaces (`IRepository`, `IRequestHandler`, `IUnitOfWork`, `INotification`), attributes (`[SensitiveData]`, `[LogIgnore]`), and message definitions (`Request`, `Response`).
2. **`SebastianGuzmanMorla.DDD`**: Implements EF Core repositories, caching decorators (`CachedRepository`), unit of work (`UnitOfWork`), and base handlers (`RequestHandler`, `RequestPageHandler`).
3. **`SebastianGuzmanMorla.DDD.Generator`**: Roslyn Source Generators to automate DI registration, sensitive property scrubbing, and EF Core entity type configurations.

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
   using SebastianGuzmanMorla.DDD.Repositories;

   public class CustomerRepository(IServiceProvider serviceProvider) 
       : Repository<MyDbContext, Customer>(serviceProvider), ICustomerRepository
   {
       public async Task<Customer?> GetByEmail(string email, CancellationToken cancellationToken = default)
       {
           return await Queryable.FirstOrDefaultAsync(x => x.Email == email, cancellationToken);
       }
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

### 2. Request Handlers
Implement handlers inheriting from `RequestHandler<TContext, TRequest, TResponse>`:
- The base handler wraps `Execute` inside a database transaction managed by `IUnitOfWork<TContext>`.
- If an `IValidator<TRequest>` is registered in the dependency injection container, the handler automatically performs validation before invoking `Execute`. If validation fails, it returns a `400 BadRequest` response with errors.

```csharp
using SebastianGuzmanMorla.DDD.Handlers;

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

## 5. Entity Framework Core Configuration Mapping

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

## 6. Summary of Design Patterns & Conventions

| Target | Convention | Source Generator Effect |
| :--- | :--- | :--- |
| **Domain Entities** | Inherit `Entity`, use Guid version 7, partial if they require custom EF mappings. | None |
| **Repositories** | Inherit interface `IRepository<T>`, implement class `Repository<TContext, T>`. | Registered via `ConfigureRepositoryServices.ConfigureGenerated(services)`. |
| **Requests** | Inherit `Request<TResponse>`, mark class `partial`. | Scrubs `[SensitiveData]` fields in generated `ClearSensitiveProperties()`. |
| **Request Handlers**| Inherit `RequestHandler<TContext, TReq, TRes>`. | Registered via `ConfigureHandlerServices.ConfigureGenerated(services)`. |
| **EF Configurations**| Implement `IEntityTypeConfiguration<T>`. | Hooked via `modelBuilder.ApplyGeneratedConfigurations()`. |
| **Notifications** | Implement `INotification`, handlers implement `INotificationHandler<T>`. | Registered as singletons automatically. |
