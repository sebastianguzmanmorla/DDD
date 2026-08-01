# 20. Identity & Audit Context Pattern (`IIdentityContext`)

To track security actor context (`UserId`, `OrganizationId`, `ClientId`, `DeviceId`) across CQRS handlers and database audit logs, the solution resolves security claims from `IHttpContextAccessor`.

---

## A. Define the Interface (`[Project].Contracts/Interfaces`)

```csharp
namespace MyProject.Contracts.Interfaces;

public interface IIdentityContext
{
    Guid UserId { get; }
    Guid OrganizationId { get; }
    Guid ClientId { get; }
    Guid DeviceId { get; }
    Guid ClientTokenId { get; }
}
```

---

## B. Implement the Identity Context Service (`[Project].Web/Services`)

Implement `IIdentityContext` resolving claim types from `IHttpContextAccessor`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MyProject.Contracts.Interfaces;

namespace MyProject.Web.Services;

public class IdentityContextService(IHttpContextAccessor httpContextAccessor) : IIdentityContext
{
    private readonly HttpContext? _httpContext = httpContextAccessor.HttpContext;

    public Guid UserId => GetGuidClaim(ClaimTypes.NameIdentifier) ?? GetGuidClaim("sub") ?? Guid.Empty;
    public Guid OrganizationId => GetGuidClaim("organization_id") ?? Guid.Empty;
    public Guid ClientId => GetGuidClaim("azp") ?? Guid.Empty;
    public Guid DeviceId => GetGuidClaim("device_id") ?? Guid.Empty;
    public Guid ClientTokenId => GetGuidClaim("jti") ?? Guid.Empty;

    private Guid? GetGuidClaim(string claimType)
    {
        string? val = _httpContext?.User.Claims.FirstOrDefault(c => c.Type == claimType)?.Value;
        return Guid.TryParse(val, out Guid id) ? id : null;
    }
}
```

---

## C. Register Service in DI (`Program.cs`)

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IIdentityContext, IdentityContextService>();
```

---

## D. Usage inside CQRS Base Handlers

Base `RequestHandler` resolves `IIdentityContext` from DI during lifecycle execution (`OnAfterExecute`), linking every database transaction and `LogRequest` audit row to the calling user and organization automatically.
