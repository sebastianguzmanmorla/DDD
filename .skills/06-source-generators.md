# 6. Source Generators Integration

This solution relies on Roslyn Source Generators to eliminate boilerplate code for service registration and validator composition.

## A. CQRS Handlers & Binders Generator (`SebastianGuzmanMorla.DDD.Generator`)
Registers request handlers and binders as scoped services and notification handlers
as singletons. Discovery applies to the current compilation: declare a
`ConfigureHandlerServices` marker in each project whose handlers or binders need registration.

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

---

## B. Infrastructure Repositories Generator (`SebastianGuzmanMorla.DDD.Generator`)
Automatically registers all repositories (`Repository` or `CachedRepository`) in DI.

```csharp
namespace MyProject.Infrastructure;

public static partial class ConfigureRepositoryServices
{
    private static partial void ConfigureGenerated(IServiceCollection services);

    public static IServiceCollection ConfigureInfrastructure(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> options)
    {
        services.AddDbContext<DatabaseContext>(options);
        services.AddScoped<IUnitOfWork<DatabaseContext>, UnitOfWork<DatabaseContext>>();
        ConfigureGenerated(services); // Registers all detected repositories
        return services;
    }
}
```

---

## C. Validator Generator (`SebastianGuzmanMorla.Validator.Generator`)
Handles validator DI registration and cascades interface validation rules into concrete classes.

```csharp
public static partial class ConfigureServices
{
    private static partial void RegisterValidators(IServiceCollection services);

    public static IServiceCollection ConfigureDomain(this IServiceCollection services)
    {
        RegisterValidators(services); // Automatically registers all IValidator<T> implementations
        return services;
    }
}
```

## F. Supported Shapes and Verification

- Handler/repository registration supports file-scoped, nested, and global
  namespaces. Split partial declarations are registered once. Use a public,
  top-level static partial registration marker with the declared partial method.
- Open generic notification handlers are supported when their type parameters
  match the notification interface. Arbitrary open generic request handlers and
  repositories are not covered; prefer concrete implementations.
- Redaction supports internal, generic, and nested partial request classes and
  duplicate simple names in different namespaces. Containing types of a nested
  request must also be partial. Accessible inherited sensitive properties are
  cleared, and existing/generated base cleanup is invoked. A manual override
  remains responsible for its own redaction. Use writable sensitive properties;
  do not assume recursive redaction of nested objects.
- EF mapping generation skips abstract and open generic templates and applies
  concrete configurations, including those inheriting from generic maps.
- Verify both compilation and behavior: execute redaction, resolve generated DI
  registrations, and inspect EF metadata. Project-reference tests alone do not
  verify analyzer packaging; use the installed-package check described in
  [consumer testing](21-library-consumer-testing.md).

Concrete validators must be marked `partial` to enable interface validation cascading:
```csharp
public partial class DeviceValidator : Validator<Device>
{
}
```

---

## D. Smart Enum Generator (`SebastianGuzmanMorla.SmartEnum.Generator`)
Generates lookup, parsing, and collection properties for custom `SmartEnum` types.

```csharp
[GenerateSmartEnum]
public sealed partial class StatusType : SmartEnum<StatusType, string>
{
}
```

---

## E. Clear Sensitive Properties Generator (`SebastianGuzmanMorla.DDD.Domain.Generator`)
Clears the values of sensitive properties on Requests before they are audited/logged.

```csharp
public partial class LoginRequest : Request<LoginResponse>
{
    [SensitiveData]
    public required string Password { get; set; }
}
```

Generates:
```csharp
public partial class LoginRequest
{
    public override void ClearSensitiveProperties()
    {
        Password = default;
    }
}
```
