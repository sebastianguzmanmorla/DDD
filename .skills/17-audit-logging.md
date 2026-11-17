# 17. Audit Logging Architecture

Use the project-specific base `RequestHandler` to stage mutation audit records in the same transaction as the domain change.

## A. Database Schema
Audit records are divided into two entities (persisted to DB):
- **`LogRequest`**: Captures global request metadata (IP address, user/subject claims, target route, request method, execution timestamp, and serialized JSON request body with sensitive credentials redacted).
- **`Log`**: Captures the entity type, ID, operation and message. Add an explicit projection if the audit requires entity data.

---

## B. Implementation Pattern
Use [the CQRS mutation template](04-application-cqrs.md) to stage both the redacted `LogRequest` and its `Log` rows through repositories that share the mutation's scoped `DbContext` and `UnitOfWork`. Commit once, and set the response's `LogId` after the commit. If staging, saving, or cancellation fails before commit, roll back with `CancellationToken.None` and propagate the error.

```csharp
await UnitOfWork.CreateTransaction(cancellationToken);
try
{
    await repository.Update(cancellationToken, entity);
    Guid logId = await StageMutationAudit(request, entity, LogType.Put, "Updated entity", cancellationToken);
    await UnitOfWork.Commit(cancellationToken);
    return new Response { LogId = logId };
}
catch
{
    await UnitOfWork.Rollback(CancellationToken.None);
    throw;
}
```

Record failed attempts separately through the application's operational logging policy. Use `OnAfterExecute` only for work that can happen independently of the mutation. For an external audit destination, stage an outbox row in the mutation transaction and deliver it asynchronously.

Mark read-only requests with `[LogIgnore]` when the application's separate query logging honors that attribute.

---

## C. Request Cloning & Redaction (`ToLogEntity` Extension)
Clone the request before executing `.ClearSensitiveProperties()` so replacing its
instance property values does not change the original request. `Clone` is shallow:
nested objects remain shared, and static properties are shared across instances.
Do not mutate nested/shared state in a custom redaction override; use instance
properties for request credentials.

```csharp
public static LogRequest ToLogEntity(this Request request, IIdentityContext identity, JsonSerializerOptions jsonSerializerOptions, Guid logRequestId)
{
    Request clonedRequest = (Request)request.Clone();
    clonedRequest.ClearSensitiveProperties();

    var contextData = new
    {
        UserId = identity.UserId,
        ClientId = identity.ClientId,
        DeviceId = identity.DeviceId
    };

    return new LogRequest
    {
        Id = logRequestId,
        Context = JsonSerializer.Serialize(contextData),
        Type = request.GetType().Name,
        Request = JsonSerializer.Serialize(clonedRequest, request.GetType(), jsonSerializerOptions),
        UpdatedAt = DateTime.UtcNow
    };
}
```
