# 17. Audit Logging Architecture

Every bounded context inherits from a project-specific base `RequestHandler` that overrides execution lifecycle hooks to manage audit trails automatically.

## A. Database Schema
Audit records are divided into two entities (persisted to DB):
- **`LogRequest`**: Captures global request metadata (IP address, user/subject claims, target route, request method, execution timestamp, and serialized JSON request body with sensitive credentials redacted).
- **`Log`**: Captures granular entity state changes and events logged during request execution (reference entity Type, ID, change Type, and serialized data).

---

## B. Implementation Pattern
Base `RequestHandler` manages transactions and maps logs:

```csharp
protected override async Task OnAfterExecute(TRequest request, TResponse response, CancellationToken cancellationToken = default)
{
    bool logIgnore = request.GetType().GetCustomAttributes(typeof(LogIgnoreAttribute), true).Length != 0;
    bool logError = _logs.Any(x => x.Type == LogType.Error);

    // Skip logging for query operations marked with [LogIgnore] unless an error occurred
    if (!logError && (_logs.Count == 0 || logIgnore)) return;

    await using (UnitOfWork)
    {
        try
        {
            await UnitOfWork.CreateTransaction(cancellationToken);

            LogRequest logRequest = request.ToLogEntity(IdentityContext, _jsonSerializerOptions, LogRequestId);
            await _logRequestRepository.Add(cancellationToken, logRequest);

            response.LogId = logRequest.Id;

            List<Log> logEntries = _logs
                .Select(x => x.ToLogEntity(_jsonSerializerOptions, logRequest.Id))
                .ToList();

            await _logRepository.Add(cancellationToken, logEntries);
            await UnitOfWork.Commit(cancellationToken);
        }
        catch (Exception ex)
        {
            AddLog(LogType.Error, ex.ToString());
            await UnitOfWork.Rollback(cancellationToken);
        }
    }
}
```

* **RULE**: Mark query requests (which read data and do not modify state) with `[LogIgnore]` to avoid flooding the database with audit rows.

---

## C. Request Cloning & Redaction (`ToLogEntity` Extension)
Clones the request object first before executing `.ClearSensitiveProperties()`, ensuring sensitive credentials (like passwords) are redacted from database logs without mutating state in active execution threads:

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
