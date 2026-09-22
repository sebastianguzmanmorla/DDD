# 19. Custom Request Binders (`IRequestBinder`)

For endpoints that parse inputs from form fields, URL-encoded bodies, query strings, headers, or cookies, implement a custom request binder.

---

## A. Implementing the Binder (`[Project].Web/Binders`)
Implement `IRequestBinder<TRequest, TErrorResponse>`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Http;
using SebastianGuzmanMorla.DDD.Interfaces;
using SebastianGuzmanMorla.SmartEnum;

namespace MyProject.Web.Binders;

public class LoginRequestBinder(IHttpContextAccessor httpContextAccessor)
    : IRequestBinder<LoginRequest, ErrorResponse>
{
    public async Task<(LoginRequest?, ErrorResponse?)> BindAsync(CancellationToken cancellationToken = default)
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null) 
            return (null, new ErrorResponse { Status = HttpStatusCode.InternalServerError, Message = "Internal server error" });

        if (!httpContext.Request.HasFormContentType) 
            return (null, new ErrorResponse { Status = HttpStatusCode.BadRequest, Message = "Invalid content type." });

        IFormCollection form = await httpContext.Request.ReadFormAsync(cancellationToken);

        if (!DeviceType.TryParse(form["device_type"], out DeviceType? deviceType))
            return (null, new ErrorResponse { Status = HttpStatusCode.BadRequest, Message = "Invalid device type." });

        var request = new LoginRequest
        {
            Username = form["username"],
            Password = form["password"],
            DeviceType = deviceType
        };

        return (request, null);
    }
}
```

---

## B. DI Registration & Route Mapping

1. **DI Registration (`Program.cs`)**: Declare the `ConfigureHandlerServices` marker in the binder project as shown in [source generators](06-source-generators.md). The generator implements `ConfigureGenerated`; your wrapper exposes `ConfigureBinders`:
   ```csharp
   builder.Services.ConfigureBinders(); // Calls your wrapper around ConfigureGenerated
   ```

2. **Endpoint Mapping (`MapRequest`)**: Use the overload with three generic type parameters. Create the route group explicitly:
   ```csharp
   var group = app.MapGroup("/auth");
   group.MapRequest<LoginRequest, LoginResponse, ErrorResponse>(
       LoginRequest.Method,
       "/auth",
       "/login",
       "Auth"
   );
   ```

Return either a request or an error response. Set the error status explicitly:
`Response` defaults to `OK`. Binding errors short-circuit handler execution.
Pass the cancellation token to asynchronous reads and let cancellation and
unexpected exceptions propagate to middleware; do not return `ex.Message`.
The prefix argument describes the group for OpenAPI metadata, while the route
argument is relative to that group.
