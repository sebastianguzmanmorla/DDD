# 5. Infrastructure Layer & Persistence (`[Project].Infrastructure`)

The Infrastructure Layer encapsulates database access, Entity Framework Core mappings, caching mechanisms, compiled models for Native AOT, and PostgreSQL native enum integration.

---

## EF Core DbContext & Conventions Setup

In `ConfigureConventions` of your DbContext class, register converters for `SmartEnum`, `SmartEnumFlags`, UTC DateTimes, and PostgreSQL Native Enums:

```csharp
using Microsoft.EntityFrameworkCore;
using SebastianGuzmanMorla.SmartEnum.EntityFrameworkCore.Converters;
using SebastianGuzmanMorla.DDD.Infrastructure.Converters;

namespace MyProject.Infrastructure;

public class DatabaseContext(DbContextOptions<DatabaseContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<Customer> Customers { get; set; }
    public DbSet<Log> Log { get; set; }
    public DbSet<LogRequest> LogRequest { get; set; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Native PostgreSQL Enum Mapping
        configurationBuilder.Properties<LogType>()
            .HaveColumnType("log_type");

        // Mapping Single SmartEnum
        configurationBuilder.Properties<StatusType>()
            .HaveConversion<SmartEnumConverter<StatusType, string>, SmartEnumComparer<StatusType, string>>()
            .HaveColumnType("text");

        // Mapping SmartEnumFlags
        configurationBuilder.Properties<UserRoles>()
            .HaveConversion<SmartEnumFlagsValueConverter<UserRoles, RoleType, string>, SmartEnumFlagsValueComparer<UserRoles, RoleType, string>>()
            .HaveColumnType("text");

        // Built-in UTC DateTime Converters from SebastianGuzmanMorla.DDD.Infrastructure.Converters
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>();

        configurationBuilder.Properties<DateTime?>()
            .HaveConversion<NullableUtcDateTimeConverter>();
    }
}
```

---

## PostgreSQL Native Enum Mapping Pattern

When storing standard C# enums (like `LogType`) as native database enums in PostgreSQL using Npgsql:

1. **`ConfigureConventions`**: `configurationBuilder.Properties<MyEnum>().HaveColumnType("my_enum_db_type");`
2. **`OnModelCreating`**: `builder.HasPostgresEnum<MyEnum>(name: "my_enum_db_type");`
3. **DbContext Bootstrapping Callback (`MapEnums`)**:
   ```csharp
   public static void MapEnums(NpgsqlDbContextOptionsBuilder builder)
   {
       builder.MapEnum<MyEnum>("my_enum_db_type");
   }
   ```
   Pass callback to `options.UseNpgsql(connectionString, DatabaseContext.MapEnums)` during DI setup.

---

## Entity Configuration Mappings

Implement `IEntityTypeConfiguration<TEntity>` in `[Project].Infrastructure/Mappings` using the naming format `[EntityName]Map.cs`:
* **RULE**: Call `builder.ConfigureEntity();` from `SebastianGuzmanMorla.DDD.Infrastructure.Mappings` to automatically configure base `Entity` fields (primary key, CreatedAt, UpdatedAt, DeletedAt).

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

Auto-apply all mapping classes in DbContext:
```csharp
protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder);
    
    // Automatically discovers and registers all mapping classes in the assembly
    builder.ApplyGeneratedConfigurations();
}
```

---

## Value Objects Mapping

* **Complex Types Rule (`builder.ComplexProperty`)**: Map Value Objects using `builder.ComplexProperty` to flatten properties into columns on the parent entity table.

```csharp
builder.ComplexProperty(x => x.BillingAddress, a =>
{
    a.Property(p => p.Street).HasColumnName("billing_street").HasMaxLength(200);
    a.Property(p => p.City).HasColumnName("billing_city").HasMaxLength(100);
    a.Property(p => p.State).HasColumnName("billing_state").HasMaxLength(50);
    a.Property(p => p.ZipCode).HasColumnName("billing_zip").HasMaxLength(20);
});
```

* **JSON Column Options (`jsonb`)**:
  - **Option A: Native EF Core `ToJson()`**:
    ```csharp
    builder.OwnsOne(x => x.BillingAddress, b =>
    {
        b.ToJson("billing_address"); // Stores as JSON column named billing_address
    });
    ```

  - **Option B: ValueConverter with System.Text.Json**:
    ```csharp
    using System.Text.Json;

    builder.Property(x => x.BillingAddress)
        .HasColumnName("billing_address")
        .HasColumnType("jsonb")
        .HasConversion(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<Address>(v, (JsonSerializerOptions?)null)!);
    ```

---

## EF Core Compiled Models (Optimization for Native AOT)

Compile EF Core database models to eliminate reflection startup overhead:
```bash
dotnet ef dbcontext optimize --nativeaot --project src/MyProject.Infrastructure/MyProject.Infrastructure.csproj --startup-project src/MyProject.Web/MyProject.Web.csproj --context DatabaseContext
```

Register compiled static model during DbContext configuration:
```csharp
builder.Services.ConfigureInfrastructure(options =>
{
    options.UseNpgsql(connectionString)
           .UseModel(DatabaseContextModel.Instance); 
});
```

---

## Implementing Repositories (`[Project].Infrastructure/Repositories`)

Define repository interfaces in `[Project].Domain/Interfaces/Repositories` and implement them in `[Project].Infrastructure/Repositories`.

### Base Repository Mutation Methods (`Repository<TContext, TEntity>`)
All repositories derived from `Repository<TContext, TEntity>` inherit the following built-in mutation methods:

| Method | Behavior |
| --- | --- |
| `await repository.Add(cancellationToken, items)` | Sets `UpdatedAt = DateTime.UtcNow` and adds items to `DbSet`. |
| `await repository.Update(cancellationToken, items)` | Sets `UpdatedAt = DateTime.UtcNow` and updates tracking state in EF Core. |
| `await repository.SoftDelete(cancellationToken, items)` | Sets `DeletedAt = DateTime.UtcNow` and updates tracking state in EF Core. |
| `await repository.Upsert(cancellationToken, items)` | Sets `UpdatedAt = DateTime.UtcNow` and runs `UPSERT ON (id)` via `FlexLabs.EntityFrameworkCore.Upsert`. |
| `await repository.HardDelete(cancellationToken, items)` | Runs immediate bulk `ExecuteDeleteAsync()` SQL command in DB by ID collection. |

### How `Queryable` is Defined in `Repository<TContext, TEntity>`
In the base `Repository<TContext, TEntity>` class, `Queryable` is defined as:
```csharp
protected IQueryable<TEntity> Queryable =>
    UnitOfWork.Context.Set<TEntity>().AsNoTracking().Where(x => x.DeletedAt == null);
```

> [!IMPORTANT]
> Because `Queryable` automatically includes `.AsNoTracking()` and `.Where(x => x.DeletedAt == null)`, any query using `Queryable` is read-only by default and automatically excludes soft-deleted records.

---

### A. Standard Repository (`Repository<TContext, TEntity>`)
Use this for entities that do not require caching or whose state changes frequently:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MyProject.Domain.Entities;
using MyProject.Domain.Interfaces.Repositories;
using SebastianGuzmanMorla.DDD.Infrastructure.Repositories;

namespace MyProject.Infrastructure.Repositories;

public class UserRepository(
    IServiceProvider serviceProvider
) : Repository<DatabaseContext, User>(serviceProvider), IUserRepository
{
    // Custom repository method using EF Core DbContext Queryable
    public async Task<User?> GetByEmail(string email, CancellationToken cancellationToken = default)
    {
        // Queryable automatically applies .AsNoTracking() and .Where(x => x.DeletedAt == null)
        return await Queryable
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
    }
}
```

---

### B. Cached Repository (`CachedRepository<TContext, TEntity>`)
Use this for read-heavy aggregates (configurations, reference catalogs, static parameters) that benefit from Redis caching.

* **Requirements**:
  1. Specify `CacheKeyPrefix` (e.g. `"MyProject:customer"`).
  2. Specify System.Text.Json `JsonTypeInfo<TEntity>` compilation metadata from `DomainJsonSerializerContext`.

```csharp
using System.Text.Json.Serialization.Metadata;
using MyProject.Domain;
using MyProject.Domain.Entities;
using MyProject.Domain.Interfaces.Repositories;
using SebastianGuzmanMorla.DDD.Infrastructure.Repositories;

namespace MyProject.Infrastructure.Repositories;

public class CustomerRepository(
    IServiceProvider serviceProvider
) : CachedRepository<DatabaseContext, Customer>(serviceProvider), ICustomerRepository
{
    protected override string CacheKeyPrefix => "MyProject:customer";

    protected override TimeSpan CacheExpiry => TimeSpan.FromMinutes(15);

    // Link to System.Text.Json source generation metadata for reflection-free serialization
    protected override JsonTypeInfo<Customer> JsonTypeInfo => DomainJsonSerializerContext.Default.Customer;
}
```

* **Cache Invalidation Lifecycle**:
  `CachedRepository` automatically intercepts mutation operations (`Update`, `Upsert`, `SoftDelete`, `HardDelete`) and invalidates matching Redis key patterns during Unit of Work transaction commits.
