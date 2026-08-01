# 6. Source Generators Integration

This solution relies on Roslyn Source Generators to eliminate boilerplate code for service registration and validator composition.

## A. CQRS Handlers & Binders Generator (`SebastianGuzmanMorla.DDD.Generator`)
Registers all application request handlers (`RequestHandler`) and model binders (`IRequestBinder`) automatically.

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
        ConfigureGenerated(services); // Registers all detected repositories
        return services;
    }
}
```

---

## C. Validator Generator (`SebastianGuzmanMorla.Validator.Generator`)
Handles validator DI registration and cascades interface validation rules into concrete classes.

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
