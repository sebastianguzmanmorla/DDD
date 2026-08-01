---
name: ddd_development
description: Guidelines and code templates for developing Domain-Driven Design (DDD) components. Use this when creating entities, value objects, CQRS handlers, requests/responses, repositories, or extending bounded contexts.
---

# Domain-Driven Design (DDD) Development Skill

This skill guides development inside DDD-structured codebases, aligning with the architecture established by `SebastianGuzmanMorla.DDD`, `SebastianGuzmanMorla.Validator`, and `SebastianGuzmanMorla.SmartEnum` libraries.

> [!CAUTION]
> ### MANDATORY DDD RULES & NON-NEGOTIABLE ANTI-PATTERNS
> 
> 1. **NO DIRECT DBCONTEXT ACCESS IN HANDLERS / APPLICATION LAYER**:
>    - ❌ **FORBIDDEN**: NEVER inject, resolve, or access `DbContext` (or `DatabaseContext`) directly inside CQRS Handlers or Application Services to query/mutate entities.
>    - ✅ **MANDATORY**: Handlers MUST interact with the database EXCLUSIVELY through domain Repository interfaces (e.g. `ICustomerRepository`, `IRepository<Customer>`).
> 
> 2. **NO CONTRACT VALIDATOR REPOSITORY ACCESS**:
>    - ❌ **FORBIDDEN**: Contract validators (in `[Project].Contracts/Validators`) MUST NEVER access repositories, `DbContext`, or external services.
>    - ✅ **MANDATORY**: Contract validators execute syntactic checks only. Database & business rule validations MUST be placed in Domain validators (`[Project].Domain/Validators`).
> 
> 3. **NO ANEMIC DOMAIN MODELS**:
>    - ❌ **FORBIDDEN**: Do not expose public setters for domain entity state.
>    - ✅ **MANDATORY**: Mutate entity state exclusively through explicit domain behavior methods on the entity (e.g. `stage.Start()`, `stage.Complete()`).
> 
> 4. **UNIT OF WORK TRANSACTION BOUNDARIES**:
>    - ❌ **FORBIDDEN**: Never call `DbContext.SaveChangesAsync()` directly inside Handlers.
>    - ✅ **MANDATORY**: Use `await UnitOfWork.CreateTransaction(cancellationToken)`, `await UnitOfWork.Commit(cancellationToken)`, and `await UnitOfWork.Rollback(cancellationToken)`.
> 
> 5. **REPOSITORY ENTITY MUTATIONS & SOFT DELETE**:
>    - ❌ **FORBIDDEN**: Do not call EF Core `DbSet.Remove(...)` directly for domain entities or manually alter tracking states in handlers.
>    - ✅ **MANDATORY**: Persist entity state modifications exclusively via Repository methods:
>      - `await repository.Add(cancellationToken, entity);` to insert new entities.
>      - `await repository.Update(cancellationToken, entity);` to persist modified entities.
>      - `await repository.SoftDelete(cancellationToken, entity);` to soft delete entities (automatically sets `DeletedAt = DateTime.UtcNow`).
> 
> 6. **REDATING SENSITIVE DATA**:
>    - ❌ **FORBIDDEN**: Passwords, secret tokens, or PINs must never be logged in raw format.
>    - ✅ **MANDATORY**: Decorate sensitive request properties with `[SensitiveData]` and mark the Request class as `partial`.

---

## Directory Structure & Class Mapping Cheatsheet

| Layer Project | Class / Artifact Type | Target Folder Path | Class Naming Convention & Example |
| --- | --- | --- | --- |
| `[Project].Contracts` | CQRS Request DTO | `Messaging/[Module]/` | `[Action][Entity]Request.cs` (e.g. `CreateCustomerRequest.cs`) |
| `[Project].Contracts` | CQRS Response DTO | `Messaging/[Module]/` | `[Action][Entity]Response.cs` (e.g. `CreateCustomerResponse.cs`) |
| `[Project].Contracts` | Entity Data DTO | `Data/[Module]/` | `[Entity]Data.cs` (e.g. `CustomerData.cs`) |
| `[Project].Contracts` | Shared Enum / SmartEnum | `Data/Enums/` | `[EnumName].cs` (e.g. `StatusType.cs`, `Scope.cs`) |
| `[Project].Contracts` | Validation Interface | `Interfaces/[Module]/` | `I[Entity]Validation.cs` (e.g. `ICustomerIdValidation.cs`) |
| `[Project].Contracts` | Contract Syntactic Validator | `Validators/[Module]/` | `[Action][Entity]RequestValidator.cs` (e.g. `CreateCustomerRequestValidator.cs`) |
| `[Project].Domain` | Domain Entity | `Entities/[Module]/` | `[Entity].cs` (e.g. `Customer.cs`) |
| `[Project].Domain` | Value Object | `ValueObjects/` | `[ValueObject].cs` (e.g. `Address.cs`) |
| `[Project].Domain` | Repository Contract | `Interfaces/Repositories/` | `I[Entity]Repository.cs` (e.g. `ICustomerRepository.cs`) |
| `[Project].Domain` | Domain Semantic Validator | `Validators/[Module]/` | `[Action][Entity]RequestValidator.cs` (e.g. `CreateCustomerRequestValidator.cs`) |
| `[Project].Domain` | Domain Event / Notification | `Notifications/` | `[Event]Notification.cs` (e.g. `WelcomeNotification.cs`) |
| `[Project].Application` | CQRS Command/Query Handler | `Handlers/[Module]/` | `[Action][Entity]RequestHandler.cs` (e.g. `CreateCustomerRequestHandler.cs`) |
| `[Project].Application` | Aggregate Base Handler | `Handlers/[Module]/` | `[Entity]Handler.cs` (e.g. `CustomerHandler.cs`) |
| `[Project].Application` | DTO Projection Expressions | `Projections/` | `[Entity]Projections.cs` (e.g. `CustomerProjections.cs`) |
| `[Project].Application` | Notification Handler | `Notifications/` | `[Event]NotificationHandler.cs` (e.g. `WelcomeNotificationHandler.cs`) |
| `[Project].Infrastructure` | EF Core Entity Mapping | `Mappings/[Module]/` | `[Entity]Map.cs` (e.g. `CustomerMap.cs`) |
| `[Project].Infrastructure` | EF Core Repository | `Repositories/[Module]/` | `[Entity]Repository.cs` (e.g. `CustomerRepository.cs`) |
| `[Project].Web` | Minimal API Route Group | `Endpoints/` | `[Module]Endpoints.cs` (e.g. `CustomerEndpoints.cs`) |
| `[Project].Web` | Custom HTTP Request Binder | `Binders/` | `[Action]RequestBinder.cs` (e.g. `LoginRequestBinder.cs`) |

---

## Sub-Skills Index

| # | Sub-Skill | Intent / Trigger | Reference Path | Critical Rules |
| --- | --- | --- | --- | --- |
| **01** | **Domain Layer** | Create or modify Entities, Value Objects (`readonly record struct`), Soft Delete, or UTC DateTimes (`EnsureUtc`). | [.skills/01-domain-layer.md](.skills/01-domain-layer.md) | • Entities use `Guid.CreateVersion7()`.<br>• Base `Queryable` automatically applies `.AsNoTracking()` & `.Where(x => x.DeletedAt == null)`. |
| **02** | **Smart Enums** | Create custom SmartEnums (`SmartEnum<TEnum, TKey>`) or flags (`SmartEnumFlags<TFlags, TEnum, TKey>`). | [.skills/02-smart-enums.md](.skills/02-smart-enums.md) | • Annotate with `[GenerateSmartEnum]` and `[JsonConverter]`.<br>• Declare class as `sealed partial`. |
| **03** | **Validation Engines** | Create Contract or Domain validators, configure flow control (`ValidationErrorHandle`), or interface cascading (`IEntityValidation`). | [.skills/03-validation.md](.skills/03-validation.md) | • Contract validators MUST NEVER access repositories or DBs.<br>• Interface cascading validators must be declared `partial`. |
| **04** | **Application & CQRS** | Create/modify Requests, Responses, CQRS Handlers (`RequestHandler`), Domain Notifications (`INotification`), or Paginated Queries (`RequestPageHandler`). | [.skills/04-application-cqrs.md](.skills/04-application-cqrs.md) | • Handlers MUST NOT access `DbContext` directly; use Repositories.<br>• Perform mutations via `repository.Add`, `repository.Update`, `repository.SoftDelete`. |
| **05** | **Infrastructure & EF Core** | Configure DbContext conventions, PostgreSQL native enums (`log_type`), entity mappings (`builder.ConfigureEntity()`), compiled models (Native AOT), or Repositories (`Repository` / `CachedRepository`). | [.skills/05-infrastructure-persistence.md](.skills/05-infrastructure-persistence.md) | • Call `builder.ConfigureEntity()` in all entity maps.<br>• `Queryable` automatically includes `.AsNoTracking()` & `.Where(x => x.DeletedAt == null)`. |
| **06** | **Source Generators** | Register or debug Roslyn Source Generators (`ConfigureGenerated`, `RegisterValidators`, `ClearSensitiveProperties`). | [.skills/06-source-generators.md](.skills/06-source-generators.md) | • Handlers, binders, repositories, and validators use autogenerated partial extensions. |
| **07** | **Modular Localization** | Implement modular localization (`GeneralLocalization`), Resource Marker Class Pattern, or MSBuild resx configuration. | [.skills/07-modular-localization.md](.skills/07-modular-localization.md) | • Pass localized properties to `RuleLocalization` (never string literals). |
| **08** | **JSON Serialization** | Register DTOs, Entities, Requests, or Enums in reflection-free System.Text.Json contexts (`DomainJsonSerializerContext` / `ContractsJsonSerializerContext`). | [.skills/08-json-serialization.md](.skills/08-json-serialization.md) | • Every new model MUST be annotated with `[JsonSerializable]`.<br>• Register `List<T>` for `CachedRepository` Redis models. |
| **09** | **DTO Projections** | Map entities to DTOs using compiled expression trees (`Expression<Func<TEntity, TDto>>` vs `ToData` extension method). | [.skills/09-projection-mapping.md](.skills/09-projection-mapping.md) | • Expressions evaluate on DB query execution via `.Select()`. |
| **10** | **Validator Unit Testing** | Write unit tests for validators using xUnit, NSubstitute, and `ValidatorTestBase`. | [.skills/10-validator-unit-testing.md](.skills/10-validator-unit-testing.md) | • Stub `IRuleLocalization` & `IGeneralLocalization` in `ValidatorTestBase`. |
| **11** | **Web Routing (`MapRequest`)** | Centralize HTTP routes and verbs in Request DTOs and map minimal API endpoints (`group.MapRequest`). | [.skills/11-web-routing.md](.skills/11-web-routing.md) | • Request DTOs declare `Route` and `Method` constants. |
| **12** | **Policy Authorization** | Configure ASP.NET Core policy authorization using Smart Enum flags (`SmartEnumRequirementHandler`). | [.skills/12-policy-authorization.md](.skills/12-policy-authorization.md) | • Use type-safe `Scope.[Choice].PolicyName` in `RequireAuthorization`. |
| **13** | **Exception Middleware** | Register global `ExceptionHandlerMiddleware` for AOT-compatible error payloads and automatic DB logging. | [.skills/13-exception-middleware.md](.skills/13-exception-middleware.md) | • Returns 499 for cancelled requests, 400 for bad HTTP, 500 with `LogId` for generic exceptions. |
| **14** | **Cached Health Checks** | Protect database status endpoints using Redis-backed hosted health checks (`CachedHealthCheckService`). | [.skills/14-cached-health-checks.md](.skills/14-cached-health-checks.md) | • Maps `/health` endpoint to cached Redis health state. |
| **15** | **Cryptographic Hashing** | Store and verify credentials using PBKDF2 salted hashing (`SecretHasher` / `ISecretHash`). | [.skills/15-secret-hashing.md](.skills/15-secret-hashing.md) | • Use `entity.ValidateSecret("plainSecret")` extension method. |
| **16** | **Web Bootstrapping** | Wire up composition pipeline in `Program.cs` (STJ options, source-generated extensions, middleware). | [.skills/16-web-bootstrapping.md](.skills/16-web-bootstrapping.md) | • Order: STJ contexts -> Autogenerated DI extensions -> Middlewares -> Endpoints. |
| **17** | **Audit Logging** | Persist request metadata (`LogRequest`) and granular entity mutations (`Log`) inside UoW transaction hooks. | [.skills/17-audit-logging.md](.skills/17-audit-logging.md) | • Requests are cloned and redacted via `ToLogEntity` before saving. |
| **18** | **UI Validation Mapping** | Convert validation errors to Razor Pages `ModelState` (`PageModelExtensions`) using interface validation. | [.skills/18-ui-validation-mapping.md](.skills/18-ui-validation-mapping.md) | • PageModels implement validation interfaces directly. |
| **19** | **Custom Request Binders** | Handle custom binding for form data, headers, or cookies using `IRequestBinder<TRequest, TErrorResponse>`. | [.skills/19-custom-request-binders.md](.skills/19-custom-request-binders.md) | • Call `builder.Services.ConfigureBinders()` in `Program.cs`. |
| **20** | **Identity Context** | Resolve security actor metadata (`UserId`, `ClientId`, `DeviceId`) via `IIdentityData` / `IIdentityContext`. | [.skills/20-identity-context.md](.skills/20-identity-context.md) | • `IdentityContextService` parses Claims via `IHttpContextAccessor`. |
