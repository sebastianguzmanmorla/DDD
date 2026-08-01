# 1. Domain Layer (`[Project].Domain`)

The Domain Layer contains domain entities, value objects, domain events, validators, and repository interfaces. It must remain completely free of UI, EF Core, ASP.NET Core, or external database dependencies.

---

## Architectural Layers Overview

1. **Domain Layer (`[Project].Domain`)**: Pure domain models (Entities, Value Objects), interfaces, domain validators, and domain notifications.
2. **Contracts Layer (`[Project].Contracts`)**: DTOs, request/response message schemas, shared enums, and basic syntactic validators.
3. **Application Layer (`[Project].Application`)**: Handlers implementing CQRS patterns (Commands/Queries), projections, and process managers.
4. **Infrastructure Layer (`[Project].Infrastructure`)**: EF Core DbContext, entity mappings, repository implementations, and external service adapters.

---

## Creating Entities

* **Base Inheritance**: All domain entities MUST inherit from `SebastianGuzmanMorla.DDD.Domain.Entities.Entity`.
* **Automatic Base Properties**:
  - `Id`: Sequential UUID Version 7 generated via `Guid.CreateVersion7()`.
  - `CreatedAt`: UTC timestamp.
  - `UpdatedAt`: UTC timestamp.
  - `DeletedAt`: Soft delete timestamp (`DateTime?`).
* **Rich Domain Models Rule**: Do not expose public setters for domain state. Encapsulate state mutations inside explicit domain methods on the entity.

```csharp
using SebastianGuzmanMorla.DDD.Domain.Entities;

namespace MyProject.Domain.Entities;

public class ProcessStage : Entity
{
    public required Guid ProcessId { get; set; }
    public required string Name { get; set; }
    public int Order { get; set; }
    public DateTime? StartDateTime { get; private set; }
    public DateTime? EndDateTime { get; private set; }
    public ProgressStatus Status { get; private set; } = ProgressStatus.Pending;

    public void Start()
    {
        Status = ProgressStatus.InProgress;
        StartDateTime = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Complete()
    {
        Status = ProgressStatus.Completed;
        EndDateTime = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
```

---

## Value Objects Pattern

* **Rule**: Use Value Objects for descriptive concepts that do not possess a unique `Id` (e.g. `Address`, `Money`).
* **Immutability**: Value Objects must be immutable. Use C# `readonly record struct` or `record`. Validate invariants during creation inside factory methods.

```csharp
namespace MyProject.Domain.ValueObjects;

public readonly record struct Address(
    string Street,
    string City,
    string State,
    string ZipCode
)
{
    public static Address Create(string street, string city, string state, string zipCode)
    {
        if (string.IsNullOrWhiteSpace(street)) throw new ArgumentException("Street is required");
        if (string.IsNullOrWhiteSpace(city)) throw new ArgumentException("City is required");
        return new Address(street, city, state, zipCode);
    }
}
```

---

## Soft Deleting Entities & Repository Mutations

* **Soft Delete Execution**: Soft deletion is executed via the repository by calling:
  ```csharp
  await repository.SoftDelete(cancellationToken, entity);
  ```
  The base `Repository<TContext, TEntity>` handles setting `entity.DeletedAt = DateTime.UtcNow` and updating tracking in EF Core.

* **Entity Update Execution**: Entity state mutations (or updates) are persisted via:
  ```csharp
  await repository.Update(cancellationToken, entity);
  ```
  The base `Repository<TContext, TEntity>` handles setting `entity.UpdatedAt = DateTime.UtcNow` and updating tracking in EF Core.

* **Base `Queryable` Rule**: Base `Queryable` in `Repository<TContext, TEntity>` and `RequestPageHandler` automatically applies `.AsNoTracking()` and `.Where(x => x.DeletedAt == null)`.
* **Direct `DbSet` Access**: Only add `.Where(x => x.DeletedAt == null)` manually if bypassing `Queryable` by querying `DbSet` directly.

---

## UTC DateTime Enforcement

1. Always set timestamp properties using `DateTime.UtcNow`.
2. Normalize dynamic or external dates using `EnsureUtc`:
   ```csharp
   protected static DateTime EnsureUtc(DateTime value)
   {
       return value.Kind switch
       {
           DateTimeKind.Utc => value,
           DateTimeKind.Local => value.ToUniversalTime(),
           _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
       };
   }
   ```
