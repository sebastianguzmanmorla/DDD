# 11. HTTP Routing & Endpoint Mapping Pattern (`MapRequest`)

To keep controllers and minimal API route mappings clean, declarative, and maintainable, the solution centralizes routing definitions inside request DTO contracts.

---

## Centralizing Route and Method in the Request

Every CQRS request contract declares its own route and HTTP method:

```csharp
using SebastianGuzmanMorla.DDD.Domain.Attributes;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace MyProject.Contracts.Messaging.Customers;

public class GetCustomerRequest : Request<GetCustomerResponse>
{
    // Define endpoint constants directly on the contract request object
    public const string Route = "/Customers/{id:guid}";
    public const RequestMethod Method = RequestMethod.Get;

    public Guid Id { get; set; }
}
```

---

## Standard Endpoint Mapping (`MapRequest`)

Use `MapRequest<TRequest, TResponse>` to map request contracts automatically:

```csharp
using MyProject.Contracts.Messaging.Customers;
using Microsoft.AspNetCore.Routing;
using SebastianGuzmanMorla.DDD.Extensions;

namespace MyProject.Web.Endpoints;

public static class CustomerEndpoints
{
    public static void MapCustomerEndpoints(this RouteGroupBuilder group)
    {
        // Automatically maps route and method declared on contract request, 
        // binds inputs, invokes handler via DI, and returns formatted JSON Response
        group.MapRequest<GetCustomerRequest, GetCustomerResponse>(
                GetCustomerRequest.Method,
                GetCustomerRequest.Route,
                "Customers")
            .RequireAuthorization("MyPolicyName");
    }
}
```

### Parameter Binding & Response Mapping Behavior
* **`GET` / `DELETE` methods**: Parameters are bound using `[AsParameters]` from query string or route values.
* **`POST` / `PUT` / `PATCH` methods**: Request DTO is bound using `[FromBody]` from the HTTP request body.
* **`Response` output**: Successful responses with `HttpStatusCode.OK` are returned as `Results.Json`.
* **File outputs (`ResponseFileByte` / `ResponseFilePath`)**: Automatically streamed via `Results.File`.
* **Error handling**: Validation errors (`400 BadRequest`), not found (`404`), and internal errors (`500`) map automatically according to `Response.Status`.

---

## Advanced Request Mapping with Custom Binders

If a request requires custom source parsing (e.g., reading OAuth tokens from Authorization headers, form fields, or cookies), specify the binder response model:

```csharp
group.MapRequest<CustomTokenRequest, TokenResponse, ErrorResponse>(
    CustomTokenRequest.Method,
    "/oauth/token",
    "/oauth/token", // Prefixed full route
    "OAuth"
);
```
