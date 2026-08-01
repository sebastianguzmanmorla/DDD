# 12. Policy-Based Authorization via Smart Enums

To ensure authorization scopes and roles stay synchronized, configure ASP.NET Core Policy-Based Authorization policies using Smart Enum values.

---

## A. Register the Authorization Handler
Register `SmartEnumRequirementHandler<TFlags, TEnum, TValue>` as a singleton in `Program.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using SebastianGuzmanMorla.DDD.Middleware;

// Register requirement handler for Scopes (Flags enum of Scope)
builder.Services.AddSingleton<IAuthorizationHandler, SmartEnumRequirementHandler<Scopes, Scope, string>>();
```

---

## B. Define Policies using Smart Enums
Configure policies during authorization setup using `Scope.[EnumChoice].PolicyName` for type safety:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Scope.ReadCustomers.PolicyName, policy =>
        policy
            .AddAuthenticationSchemes("Bearer")
            .RequireAuthenticatedUser()
            // Require that the scope claim contains the custom smart enum flag for ReadCustomers
            .AddRequirements(new SmartEnumRequirement<Scopes, Scope, string>(Scope.ReadCustomers))
    );
});
```

---

## C. Protect Minimal API Endpoints
Apply authorization policies to endpoints mapped via `group.MapRequest`:

```csharp
group.MapRequest<GetCustomersRequest, GetCustomersResponse>(
        GetCustomersRequest.Method,
        GetCustomersRequest.Route,
        "Customers")
    .RequireAuthorization(Scope.ReadCustomers.PolicyName);
```
