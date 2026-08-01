# 4. Application Layer & CQRS (`SebastianGuzmanMorla.DDD`)

The Application Layer orchestrates the execution of business workflows following the CQRS (Command Query Responsibility Segregation) pattern. Commands mutate state, while Queries read data without modifying state.

> [!CAUTION]
> ### MANDATORY RULE: REPOSITORY EXCLUSIVITY & MUTATIONS
> Handlers **MUST INTERACT WITH THE DATABASE EXCLUSIVELY THROUGH REPOSITORIES** (e.g. `ICustomerRepository`, `IRepository<Customer>`).
> ❌ **FORBIDDEN**: Never inject, resolve, or access `DbContext` or `DatabaseContext` directly inside CQRS Handlers. Never call EF Core `DbSet.Remove(...)`.
> ✅ **MANDATORY**: Execute entity state mutations exclusively via Repository methods:
> - Insert: `await repository.Add(cancellationToken, entity);`
> - Update: `await repository.Update(cancellationToken, entity);`
> - Soft Delete: `await repository.SoftDelete(cancellationToken, entity);`

---

## CQRS Message Schemas

Create Requests and Responses in `[Project].Contracts/Messaging/[Feature]`.

### Commands & Queries
* Commands and queries inherit from `Request<TResponse>`.
* Responses inherit from `Response` (or `ResponsePage<TData>` for pagination).
* **RULE (Redacting Sensitive Data)**: Properties that contain credentials, passwords, or secret tokens **MUST** be decorated with `[SensitiveData]` (from `SebastianGuzmanMorla.DDD.Domain.Attributes`).
* **RULE (Partial Requirement)**: Any Request containing sensitive properties must be declared as a `partial class` so the Roslyn Source Generator can implement property redaction.

```csharp
using System.Text.Json.Serialization;
using SebastianGuzmanMorla.DDD.Domain.Attributes;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace MyProject.Contracts.Messaging.Sample;

public partial class CreateSampleRequest : Request<CreateSampleResponse>
{
    public required string Name { get; set; }

    [SensitiveData] // Automatically cleared from audit database logs
    public required string Password { get; set; }
}

public class CreateSampleResponse : Response
{
    public Guid Id { get; set; }
}
```

---

## File Export Responses (`ResponseFileByte` & `ResponseFilePath`)

For requests that generate downloadable files (e.g. PDFs, Excel reports, binary attachments):

### 1. Byte Array File Response (`ResponseFileByte`)
```csharp
using SebastianGuzmanMorla.DDD.Domain.Attributes;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace MyProject.Contracts.Messaging.Reports;

[LogIgnore]
public class ExportReportRequest : Request<ResponseFileByte>
{
    public const string Route = "/Reports/export";
    public const RequestMethod Method = RequestMethod.Get;
}
```

In the handler:
```csharp
return new ResponseFileByte
{
    FileBytes = pdfBytes,
    ContentType = "application/pdf",
    FileName = "Report.pdf"
};
```

### 2. Disk File Path Response (`ResponseFilePath`)
```csharp
return new ResponseFilePath
{
    FilePath = "/tmp/downloads/Report.xlsx",
    ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    FileName = "Report.xlsx"
};
```
* Note: `group.MapRequest` automatically converts `ResponseFileByte` and `ResponseFilePath` into `Results.File(...)` HTTP stream results.

---

## Base CQRS Request Handlers

Create request handlers under `[Project].Application/Handlers` (or `[Project].CrossCutting/Handlers`).
Base handlers inherit from `SebastianGuzmanMorla.DDD.Infrastructure.Handlers.RequestHandler<TContext, TRequest, TResponse>`:

### Pre-injected Common Services & Properties
* **`GeneralLocalization` (`IGeneralLocalization`)**: Pre-injected for standardized localized message strings.
* **`IdentityContext` (`IIdentityContext`)**: Pre-injected for caller security claims (`UserId`, `OrganizationId`, `ClientId`, `DeviceId`).
* **`LogRequestId` (`Guid.CreateVersion7()`)**: Pre-generated sequential GUID v7 tracing ID. Mutation handlers can pass this ID to background tasks or external message queues before execution finishes.
* **Audit Helpers**:
  * `AddEntityLog(LogType.Put, entity, "Log text")`: Logs entity state mutations (`LogType.Put` for Create/Update, `LogType.Delete` for Soft Delete).
  * `AddLog(LogType.Information, "Log text")`: Logs general information or non-entity operational events.
  * `AddLog(LogType.Error, ex.ToString())`: Logs exceptions.

### Base Handler Implementation Template

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Attributes;
using SebastianGuzmanMorla.DDD.Domain.Entities;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.DDD.Domain.Interfaces.Repositories;
using SebastianGuzmanMorla.DDD.Domain.Messaging;
using SebastianGuzmanMorla.DDD.Domain.Notifications;
using MyProject.Contracts.Interfaces;
using MyProject.Contracts.Interfaces.Localization;
using MyProject.Infrastructure;

namespace MyProject.Application.Handlers;

public abstract class RequestHandler<TRequest, TResponse>(
    IServiceProvider serviceProvider
) : SebastianGuzmanMorla.DDD.Infrastructure.Handlers.RequestHandler<DatabaseContext, TRequest, TResponse>(serviceProvider)
    where TRequest : Request<TResponse>
    where TResponse : Response, new()
{
    protected readonly IGeneralLocalization GeneralLocalization = serviceProvider.GetRequiredService<IGeneralLocalization>();
    protected readonly IIdentityContext IdentityContext = serviceProvider.GetRequiredService<IIdentityContext>();

    private readonly JsonSerializerOptions _jsonSerializerOptions = serviceProvider.GetRequiredService<JsonSerializerOptions>();
    private readonly ILogRepository _logRepository = serviceProvider.GetRequiredService<ILogRepository>();
    private readonly ILogRequestRepository _logRequestRepository = serviceProvider.GetRequiredService<ILogRequestRepository>();

    private readonly List<INotificationLog> _logs = [];

    protected Guid LogRequestId { get; } = Guid.CreateVersion7();

    protected override async Task OnException(TRequest request, Exception exception, CancellationToken cancellationToken = default)
    {
        AddLog(LogType.Error, exception.ToString());
        await Task.CompletedTask;
    }

    protected override async Task OnAfterExecute(TRequest request, TResponse response, CancellationToken cancellationToken = default)
    {
        bool logIgnore = request.GetType().GetCustomAttributes(typeof(LogIgnoreAttribute), true).Length != 0;
        bool logError = _logs.Any(x => x.Type == LogType.Error);

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

    protected void AddEntityLog<TEntity>(LogType logType, TEntity entity, string logText = "") where TEntity : Entity
    {
        _logs.Add(new NotificationLog<TEntity>
        {
            ReferenceId = entity.Id,
            ReferenceData = entity,
            Type = logType,
            Message = logText
        });
    }

    protected void AddLog<TData>(LogType logType, TData data, string logText = "")
    {
        _logs.Add(new NotificationLog<TData>
        {
            ReferenceData = data,
            Type = logType,
            Message = logText
        });
    }

    protected void AddLog(LogType logType, string logText)
    {
        _logs.Add(new NotificationLog<TRequest>
        {
            Type = logType,
            Message = logText
        });
    }
}
```

---

## Concrete Handler Implementations

### 1. Insert Handler Example (`repository.Add`)
```csharp
public class CreateSampleRequestHandler(
    IServiceProvider serviceProvider,
    IRepository<SampleEntity> sampleRepository
) : RequestHandler<CreateSampleRequest, CreateSampleResponse>(serviceProvider)
{
    protected override async Task<CreateSampleResponse> Execute(CreateSampleRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await UnitOfWork.CreateTransaction(cancellationToken);

            var entity = new SampleEntity { Name = request.Name };

            // Add entity via Repository
            await sampleRepository.Add(cancellationToken, entity);
            
            await UnitOfWork.Commit(cancellationToken);

            AddEntityLog(LogType.Put, entity, "Created sample entity");

            return new CreateSampleResponse { Id = entity.Id };
        }
        catch (Exception ex)
        {
            await UnitOfWork.Rollback(cancellationToken);
            AddLog(LogType.Error, ex.ToString());
            return new CreateSampleResponse { Status = HttpStatusCode.InternalServerError, Message = ex.Message };
        }
    }
}
```

### 2. Update Handler Example (`repository.Update`)
```csharp
public class UpdateSampleRequestHandler(
    IServiceProvider serviceProvider,
    IRepository<SampleEntity> sampleRepository
) : RequestHandler<UpdateSampleRequest, Response>(serviceProvider)
{
    protected override async Task<Response> Execute(UpdateSampleRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await UnitOfWork.CreateTransaction(cancellationToken);

            SampleEntity? entity = await sampleRepository.FirstOrDefault(request.Id, cancellationToken);
            if (entity is null) return new Response { Status = HttpStatusCode.NotFound, Message = GeneralLocalization.NotFound };

            // Domain behavior state mutation
            entity.UpdateDescription(request.Description);

            // Persist mutation via Repository.Update (automatically sets UpdatedAt = DateTime.UtcNow)
            await sampleRepository.Update(cancellationToken, entity);

            await UnitOfWork.Commit(cancellationToken);

            AddEntityLog(LogType.Put, entity, "Updated sample entity");

            return new Response();
        }
        catch (Exception ex)
        {
            await UnitOfWork.Rollback(cancellationToken);
            AddLog(LogType.Error, ex.ToString());
            return new Response { Status = HttpStatusCode.InternalServerError, Message = ex.Message };
        }
    }
}
```

### 3. Soft Delete Handler Example (`repository.SoftDelete`)
```csharp
public class DeleteSampleRequestHandler(
    IServiceProvider serviceProvider,
    IRepository<SampleEntity> sampleRepository
) : RequestHandler<DeleteSampleRequest, Response>(serviceProvider)
{
    protected override async Task<Response> Execute(DeleteSampleRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await UnitOfWork.CreateTransaction(cancellationToken);

            SampleEntity? entity = await sampleRepository.FirstOrDefault(request.Id, cancellationToken);
            if (entity is null) return new Response { Status = HttpStatusCode.NotFound, Message = GeneralLocalization.NotFound };

            // Execute Soft Delete via Repository (automatically sets DeletedAt = DateTime.UtcNow)
            await sampleRepository.SoftDelete(cancellationToken, entity);

            await UnitOfWork.Commit(cancellationToken);

            AddEntityLog(LogType.Delete, entity, "Soft-deleted sample entity");

            return new Response();
        }
        catch (Exception ex)
        {
            await UnitOfWork.Rollback(cancellationToken);
            AddLog(LogType.Error, ex.ToString());
            return new Response { Status = HttpStatusCode.InternalServerError, Message = ex.Message };
        }
    }
}
```

---

## Aggregate-Specific Base Handlers

To reduce repetitive repository resolution calls (`serviceProvider.GetRequiredService<T>()`), bounded contexts provide specialized abstract handlers that pre-inject all relevant aggregate repositories:

```csharp
namespace MyProject.Application.Handlers.Customers;

public abstract class CustomerHandler<TRequest, TResponse>(
    IServiceProvider serviceProvider
) : RequestHandler<TRequest, TResponse>(serviceProvider)
    where TRequest : Request<TResponse>
    where TResponse : Response, new()
{
    protected readonly ICustomerRepository CustomerRepository = serviceProvider.GetRequiredService<ICustomerRepository>();
    protected readonly IAddressRepository AddressRepository = serviceProvider.GetRequiredService<IAddressRepository>();
}
```

---

## CQRS Handler Execution Workflow

The base `RequestHandler` automatically manages the request execution lifecycle:
1. **Automated Request Validation**: Before executing the handler, it resolves `IValidator<TRequest>` from the DI container (if registered). If validation fails, it halts execution and automatically returns a `400 BadRequest` response containing the collection of validation errors.
2. **Execute**: Calls your overridden `Execute(TRequest request, CancellationToken cancellationToken)`.
3. **OnAfterExecute**: Lifecycle hook executing post-operations (such as saving audit `LogRequest` and entity `Log` records inside a UoW transaction).
4. **Notification Dispatch**: Iterates over all notifications registered during execution and dispatches them.

---

## Domain Notifications & Events

Notifications represent side-effects or domain events (e.g., sending emails, raising external queue messages) that execute asynchronously after the core database transaction is committed.

### 1. Define Notification (`[Project].Domain/Notifications`)
Inherit from `INotification` and implement the double-dispatch `Handle` pattern:
```csharp
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;

namespace MyProject.Domain.Notifications;

public class WelcomeNotification : INotification
{
    public required string Email { get; init; }

    public Task Handle(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        var handler = serviceProvider.GetRequiredService<INotificationHandler<WelcomeNotification>>();
        return handler.Handle(this, cancellationToken);
    }
}
```

### 2. Implement Notification Handler (`[Project].Application/Notifications`)
```csharp
using MyProject.Domain.Notifications;
using Microsoft.Extensions.Logging;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;

namespace MyProject.Application.Notifications;

public class WelcomeNotificationHandler(
    ILogger<WelcomeNotificationHandler> logger
) : INotificationHandler<WelcomeNotification>
{
    public async Task Handle(WelcomeNotification notification, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Welcome email dispatched to {Email}", notification.Email);
        await Task.CompletedTask;
    }
}
```

### 3. Raise Notifications inside Handlers
Register the notification inside the `Execute` method using `AddNotification`:
```csharp
AddNotification(new WelcomeNotification { Email = request.Email });
```

---

## Paginated Queries (`RequestPageHandler`)

For listing and searching data, use `RequestPageHandler<TContext, TRequest, TResponse, TEntity, TResponseEntity>`.
* **Note**: `RequestPageHandler` automatically filters out soft-deleted entities (`DeletedAt == null`) and performs asynchronous `CountAsync()`, `Skip()`, and `Take()` pagination.

### 1. Paginated Request & Response
```csharp
using SebastianGuzmanMorla.DDD.Domain.Attributes;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace MyProject.Contracts.Messaging.Customers;

[LogIgnore] // Query handlers bypass audit logging via [LogIgnore]
public class GetCustomersRequest : RequestPage<GetCustomersResponse>
{
    public const string Route = "/Customers";
    public const RequestMethod Method = RequestMethod.Get;

    public string? Name { get; set; }
}

public class GetCustomersResponse : ResponsePage<CustomerData>;
```

### 2. Paginated Handler Implementation
```csharp
using MyProject.Application.Handlers;
using MyProject.Application.Projections;
using MyProject.Contracts.Data;
using MyProject.Domain.Entities;
using MyProject.Infrastructure;
using MyProject.Contracts.Messaging.Customers;

namespace MyProject.Application.Handlers.Customers;

public class GetCustomersRequestHandler(
    IServiceProvider serviceProvider
) : RequestPageHandler<DatabaseContext, GetCustomersRequest, GetCustomersResponse, Customer, CustomerData>(serviceProvider)
{
    protected override IQueryable<CustomerData> PageQuery(GetCustomersRequest request)
    {
        IQueryable<Customer> query = Queryable;

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            query = query.Where(x => x.Name.Contains(request.Name));
        }

        return query
            .OrderBy(x => x.Name)
            .Select(CustomerProjections.ToCustomerDataExpression);
    }
}
```
