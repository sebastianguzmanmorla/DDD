# 9. Projection Mapping Pattern (Entity <-> DTO)

To translate entities to contract DTOs and vice-versa cleanly without third-party mapping reflections, create static projection helper classes in `[Project].Application/Projections` (or `[Project].CrossCutting/Projections`).

---

## Projection Patterns

### 1. Database Expression Projection (`ToDataExpression`)
Use `Expression<Func<TEntity, TDto>>` inside EF Core queries (`.Select()`) so mapping evaluates directly on the database server without fetching unnecessary columns.

```csharp
using System.Linq.Expressions;
using MyProject.Contracts.Data;
using MyProject.Domain.Entities;

namespace MyProject.Application.Projections;

public static class CustomerProjections
{
    // Database expression evaluated on SQL server via .Select(CustomerProjections.ToDataExpression)
    public static Expression<Func<Customer, CustomerData>> ToDataExpression => customer => new CustomerData
    {
        Id = customer.Id,
        Name = customer.Name,
        CreatedAt = customer.CreatedAt
    };
}
```

### 2. In-Memory Extension Mapping (`ToData`)
Use static extension methods `ToData(this TEntity entity)` for mapping entities to DTOs in memory:

```csharp
public static class CustomerProjections
{
    // In-memory extension mapping
    public static CustomerData ToData(this Customer customer)
    {
        return new CustomerData
        {
            Id = customer.Id,
            Name = customer.Name,
            CreatedAt = customer.CreatedAt
        };
    }
}
```
