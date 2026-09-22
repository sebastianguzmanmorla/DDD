# 13. Global Exception Handling Middleware

Register `ExceptionHandlerMiddleware<TContext>` for unified error responses and audit logging. For reflection-free serialization, include `Response` in the configured JSON context; Native AOT publishing requires separate verification.

---

## A. Middleware Execution Lifecycle
If the response has already started, the middleware rethrows the original exception without rewriting headers or appending JSON. Otherwise:

1. **`OperationCanceledException` (including `TaskCanceledException`) with a cancelled `HttpContext.RequestAborted`**: Maps to HTTP status `499 Client Closed Request`, without an error body. Cancellation without a client abort is handled as a server error.
2. **`BadHttpRequestException`**: Maps to HTTP status `400 BadRequest` returning a standard JSON response with validation/http details.
3. **Generic `Exception`**: 
   * Maps to HTTP status `500 InternalServerError`.
   * Automatically creates a new `Log` entity with `LogType.Error` and a generated UUID Version 7 (`Guid.CreateVersion7()`).
   * Persists log record to the database (using `TContext`).
   * Returns `Message = "Internal server error"` and the persisted record's `LogId`. Exception details remain in the audit log.
   * If audit persistence fails, still returns the safe 500 response, without a `LogId`.

---

## B. Registering the Middleware in `Program.cs`

Add it right after building the WebApplication:

```csharp
using SebastianGuzmanMorla.DDD.Infrastructure.Middleware;
using MyProject.Infrastructure;

WebApplication app = builder.Build();

// Register ExceptionHandlerMiddleware early in the pipeline
app.UseMiddleware<ExceptionHandlerMiddleware<DatabaseContext>>();
```
