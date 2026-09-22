# Consumer Test Sample Based on the Skills

This sample turns the patterns in [ddd-development](../../SKILL.md) into code
compiled and executed by the tests. These four projects are not published:

- **Consumer.Contracts:** requests, responses, DTOs, syntax validation, and a
  generated JSON context. It does not reference infrastructure or repositories.
- **Consumer.Domain:** the `Customer` entity with its constructor and `Rename`
  method, a repository contract, business validation, and a generated JSON context.
- **Consumer.Infrastructure:** the repository, DbContext, mappings, UTC converters,
  and generated repository registration.
- **Consumer.Application:** handlers, projections, and generated handler registration.

**Consumer.Integration.Tests** exercises these layers with real PostgreSQL and Redis
containers. Its cached repository adapter uses the consumer entity, repository
interface, EF mapping, and generated JSON context. The fixture replaces the default
repository registration explicitly; the sample's generated registrations remain exercised.

Tests are grouped by the library project they exercise:
[consumer workflows](../SebastianGuzmanMorla.DDD.Infrastructure.Tests/Consumption),
[domain serialization](../SebastianGuzmanMorla.DDD.Domain.Tests/Consumption),
[domain validation](../SebastianGuzmanMorla.DDD.Domain.Tests/Validation),
[pagination](../SebastianGuzmanMorla.DDD.Tests/Validation), and
[generated registrations](../SebastianGuzmanMorla.DDD.Generator.Tests).
See [tests/README.md](../README.md) for the complete structure.

## Mapping Skills to Tests

| Skill | Verified behavior |
| --- | --- |
| [01: domain](../../.skills/01-domain-layer.md) | Constructor and domain methods; name without a public setter; persistence and reads with soft deletion. |
| [03: validation](../../.skills/03-validation.md) | Contract validation without repository queries; domain uniqueness checks; `StopAll`; cancellation token forwarding; `IPageValidation` composition. |
| [04: CQRS](../../.skills/04-application-cqrs.md) | Request → handler resolved through DI → repository → UoW; creation, updates, deletion, 400, 404, rollback, and the next operation. |
| [05: persistence](../../.skills/05-infrastructure-persistence.md) | `ConfigureEntity`, generated mappings, UTC values on reads, and repository soft deletion. |
| [06: generators](../../.skills/06-source-generators.md) | Repository/handler `ConfigureGenerated`, `RegisterValidators`, redaction, and validator composition executed by the compiler. |
| [08: JSON](../../.skills/08-json-serialization.md) | Generated contexts for requests, responses, DTOs, entities, and lists; round trips for entities with private state. |
| [09: projections](../../.skills/09-projection-mapping.md) | Entity-to-DTO expression executed by the paginated query. |
| [10: tests](../../.skills/10-validator-unit-testing.md) | xUnit, NSubstitute, `ValidatorTestBase`, configured localization, and small inputs for interface validation. |
| [11: routing](../../.skills/11-web-routing.md) | All five HTTP methods, query/body binding, grouped routes, redirects, and file responses. |
| [12: authorization](../../.skills/12-policy-authorization.md) | Authenticated policies, missing/invalid/repeated scopes, and custom claim types. |
| [13: middleware](../../.skills/13-exception-middleware.md) | Client cancellation, safe errors, started responses, and audit failure recovery. |
| [17: auditing](../../.skills/17-audit-logging.md) | Request cloning/redaction and exception audit persistence with a correlating response LogId. |
| [19: binders](../../.skills/19-custom-request-binders.md) | Successful custom binding and short-circuiting binding failures before handler execution. |

## Details for Consuming the Current API

- `GetCustomersRequest` explicitly implements `IPageValidation`: the base
  `RequestPage<TResponse>` class does not implement that interface. The concrete
  validator is `partial` so the generator can compose `PageValidator`.
- Validation errors use paths such as `$.Name` and `$.Size`; successful
  validation returns `null` for `Errors`.
- `IGeneralLocalization` belongs to the consumer. `ValidatorTestBase` configures
  the labels and rules used by these cases, without adding another localization
  service to the library.
- Fixtures and persistence assertions may inspect EF directly.
  Concrete handlers use repositories and the unit of work for mutations.
- JSON contexts include constructor metadata to rebuild entities with private
  setters without exposing their state to consumers.

## Running Tests and Scope

From the repository root:

```bash
dotnet test SebastianGuzmanMorla.DDD.slnx -c Release
```

Unit tests retain SQLite in memory and NSubstitute for focused regressions.
Integration tests require a running Docker daemon and use Testcontainers 4.15.0
with `postgres:17.6-alpine` and `redis:7.4.5-alpine`. Ports are assigned dynamically;
containers and connections are disposed after each test class. Rows have unique
names/IDs so tests do not depend on execution order. Nothing connects to a host database.

The 12 integration cases cover:

- Create/rename/soft delete across scopes, UTC timestamps, and real Redis invalidation.
- Entity serialization through Redis, cache TTL, expiration, and repopulation.
- Uncommitted SQL visibility, explicit rollback, and rollback on scope disposal.
- Cache bypass during a transaction and preservation of committed values after rollback.
- PostgreSQL uniqueness failures, recovery in the same unit of work, and competing inserts.
- PostgreSQL upsert/hard delete and cache invalidation after commit.
- Domain validation and cancellation without persistence.
- Redis shutdown during a transaction: the committed database write remains durable.

```bash
make test-integration
make test-unit       # No Docker required
make test-package    # Python 3 and NuGet connectivity required
```

The package smoke test packs the three libraries into a temporary feed, copies
the four consumer layers, replaces library project references with package references,
and removes explicit analyzer project references. It compiles and executes redaction,
DI registration checks, and EF mapping checks on both frameworks. Its private package
cache and temporary feed are removed afterward; nothing is published.

Native AOT publishing, distributed cache invalidation races, and database migrations
are not verified by this suite. Redis remains an eventually consistent cache;
the outage test guarantees database durability, not immediate cache recovery.
