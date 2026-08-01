# 13. Global Exception Handling Middleware

To ensure AOT-compatible error serialization and unified tracking, register `ExceptionHandlerMiddleware<TContext>` in the HTTP request pipeline.

---

## A. Middleware Execution Lifecycle
The middleware intercepts all unhandled pipeline exceptions:
1. **`TaskCanceledException`**: Maps to HTTP status `499 Client Closed Request`.
2. **`BadHttpRequestException`**: Maps to HTTP status `400 BadRequest` returning a standard JSON response with validation/http details.
3. **Generic `Exception`**: 
   * Maps to HTTP status `500 InternalServerError`.
   * Automatically creates a new `Log` entity with `LogType.Error` and a generated UUID Version 7 (`Guid.CreateVersion7()`).
   * Persists log record to the database (using `TContext`).
   * Returns a JSON payload containing `LogId` so developers can trace the exact exception stack trace in the audit database.

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
