# Error and Test Review

## Results

A corresponding xUnit test project was added for each library project, alongside
`Consumer.Integration.Tests`. The suite follows the skills and now contains
**149 cases per framework** in Release: 137 focused tests and 12 real PostgreSQL/Redis
Testcontainers tests. The previous suite contained 87 cases per framework.
The installed NuGet consumer smoke test also runs on .NET 9 and .NET 10.

Before the fixes, 21 hashing/transaction cases, 7 cache cases, and 3 redaction
cases failed. Additional cases check boundaries and compatibility.

## Fixed Issues

### 1. High: a stored hash with an empty key accepted any password

`SecretHasher.Verify` took the derived key length from the stored hash.
An empty key caused it to compare two empty sequences and return `true`,
regardless of the password. **This requires a malformed stored hash; it does
not demonstrate a bypass against valid hashes produced by `Hash`.**

Other invalid formats threw exceptions, and iteration counts were unbounded.
Verification now checks the format, a 16-byte salt, a 32-byte key, and an iteration
count between 1 and 1,000,000 before deriving the key. Constant-time comparison
and the format produced by `Hash` are preserved.

Compatibility: imported hashes with different sizes or more than 1,000,000
iterations are now rejected. Tests cover current hashes and hashes with 10,000
iterations and the same sizes.

Code: [SecretHasher.cs](../src/SebastianGuzmanMorla.DDD/SecretHasher.cs).
Tests: [SecretHasherTests.cs](../tests/SebastianGuzmanMorla.DDD.Tests/SecretHasherTests.cs).

### 2. High: a discarded transaction contaminated the next commit

`Dispose` and `DisposeAsync` rolled back the SQL transaction but retained pending
entities and post-commit actions. When the unit of work was reused, for example
to save audit records after an error, the next commit could persist abandoned
changes and run their actions.

Cleanup now removes both kinds of pending state. Rollback releases the transaction
and clears state through `finally`. Tests cover synchronous and asynchronous
disposal, database persistence, callbacks, commit, and rollback.

Consumer tests detected a regression in the initial fix: disposing a DI scope
without having used EF caused cleanup to initialize context services after the
scope had closed. Disposal now accesses the context only to roll back an active
transaction. Two additional cases check synchronous and asynchronous scope
disposal with an unused unit of work.

Code: [UnitOfWork.cs](../src/SebastianGuzmanMorla.DDD.Infrastructure/Repositories/UnitOfWork.cs).
Tests: [UnitOfWorkTests.cs](../tests/SebastianGuzmanMorla.DDD.Infrastructure.Tests/UnitOfWorkTests.cs).

### 3. High: the shared cache exposed uncommitted data

A read after `SaveChangesAsync` inside a transaction published data to Redis
before commit. A subsequent rollback did not remove that entry. Redis could also
return an older version of a row modified or deleted within the transaction.

`Any` and `FirstOrDefault` now query the database exclusively while the unit of
work has an active transaction. Outside a transaction, they retain caching.
This protects transactions started through `IUnitOfWork`; it does not detect
external transactions or expose changes EF has not yet sent to the database.

### 4. High: deferred sequences caused incorrect cache entries or invalidation

Mutations enumerated `IEnumerable<TEntity>` once for persistence and again to
update Redis. A sequence such as `Select(_ => new Entity())` generated different
IDs on each enumeration. The cache could store nonexistent entities or retain
stale entries.

`Add`, `Update`, `Upsert`, `SoftDelete`, and `HardDelete` now materialize the
input once. Tests include deferred sequences and execution after commit.

Code for issues 3 and 4:
[CachedRepository.cs](../src/SebastianGuzmanMorla.DDD.Infrastructure/Repositories/CachedRepository.cs).
Tests: [CachedRepositoryTests.cs](../tests/SebastianGuzmanMorla.DDD.Infrastructure.Tests/CachedRepositoryTests.cs).

### 5. High: the generator skipped inherited secrets and base-class cleanup

The generator only examined properties declared on the concrete class and
omitted calls to existing base-class cleanup. It could leave passwords intact
when preparing a request for auditing.

It now examines accessible inherited properties and invokes the existing
base-class implementation. Public inherited properties are accessed through
their declaring type, avoiding accidental cleanup of a property that hides them.
Tests compile and execute generated code, check that secrets are removed, and
verify that nonsensitive data is preserved. Cases cover declared, inherited,
protected, static, and hidden properties, along with manual base-class cleanup.

Code: [ClearSensitivePropertiesGenerator.cs](../src/SebastianGuzmanMorla.DDD.Domain.Generator/ClearSensitivePropertiesGenerator.cs).
Tests: [SensitiveDataGeneratorTests.cs](../tests/SebastianGuzmanMorla.DDD.Domain.Generator.Tests/SensitiveDataGeneratorTests.cs).

The extended tests also caught secrets retained in a private base property when
both the base and derived cleanup methods were generated in the same compilation.
The derived method now calls the generated base implementation.

### 6. Handler cancellation, notifications, and exception details

Request cancellation was caught as an ordinary error and returned as HTTP 500.
Queued notifications were delivered after failed operations and retained between
calls, causing repeated delivery when a scoped handler was reused. Validation and
execution exceptions also returned their full messages to clients.

Cancellation now propagates when the request token is cancelled. Notifications
are dispatched only for 2xx responses and cleared in `finally`, including errors
and cancellation. Notification failures retain the existing best-effort behavior;
request cancellation is propagated. Error responses use `Internal server error`,
while `OnException` still receives the original exception and `OnAfterExecute`
can persist an audit record. These are observable behavior changes for consumers.

Tests: [RequestHandlerTests.cs](../tests/SebastianGuzmanMorla.DDD.Infrastructure.Tests/Handlers/RequestHandlerTests.cs).

### 7. High: failed file responses included file contents in JSON

Casting a file response to `Response` did not suppress runtime polymorphic JSON
serialization. A failed response could expose `Bytes` or an internal file path.
The error branch now creates a base response containing only status, message,
errors, timestamp, and LogId. Successful byte/path downloads retain their behavior.

Tests: [EndpointTests.cs](../tests/SebastianGuzmanMorla.DDD.Tests/Web/EndpointTests.cs).

### 8. Authorization threw on invalid scopes and ignored subsequent claims

An unknown smart-enum scope raised an exception rather than denying access.
The handler inspected only the first matching claim. It now examines matching
claims and treats an unrecognized claim as granting no permissions. Authentication
remains a separate policy requirement, tested alongside scope authorization.

Tests: [AuthorizationTests.cs](../tests/SebastianGuzmanMorla.DDD.Tests/Security/AuthorizationTests.cs).

### 9. Exception middleware misclassified cancellation and rewrote started responses

The middleware handled only `TaskCanceledException` as cancellation, even when
the client had not disconnected. It now returns 499 for `OperationCanceledException`
only when `RequestAborted` is cancelled, and handles upstream/server cancellation
as an error. When the response has started, it rethrows the original exception
instead of attempting to replace headers or append JSON. Tests also verify audit
persistence, safe error messages, and recovery when audit storage fails.

Tests: [ExceptionHandlerMiddlewareTests.cs](../tests/SebastianGuzmanMorla.DDD.Infrastructure.Tests/Middleware/ExceptionHandlerMiddlewareTests.cs).

### 10. Generators emitted invalid or duplicate code for additional type shapes

Redaction now preserves accessibility, containing partial types and generic
parameters, handles global namespaces, deduplicates partial declarations, and
uses stable unique hint names for types with the same simple name.
Handler/repository generators now use semantic namespace names, support global
namespaces, emit fully qualified type references, and avoid duplicate registration
from split declarations. Mapping generation ignores open generic templates while
applying concrete configurations inherited from them.

Tests compile generated assemblies and execute redaction, resolve services to
check lifetimes, and inspect EF mapping metadata. The package smoke test separately
checks the same layered consumer without source-project analyzer references.

Tests: [RequestShapeTests.cs](../tests/SebastianGuzmanMorla.DDD.Domain.Generator.Tests/RequestShapeTests.cs),
[RegistrationShapeTests.cs](../tests/SebastianGuzmanMorla.DDD.Generator.Tests/RegistrationShapeTests.cs),
and [MappingGeneratorTests.cs](../tests/SebastianGuzmanMorla.DDD.Generator.Tests/MappingGeneratorTests.cs).

## Running Tests and Checking Dependencies

```bash
dotnet test SebastianGuzmanMorla.DDD.slnx -c Release
make test-package
dotnet list SebastianGuzmanMorla.DDD.slnx package --vulnerable --include-transitive
```

The NuGet audit reported no vulnerable packages among the dependencies resolved
during the review. Tests pin `SQLitePCLRaw.bundle_e_sqlite3` to 3.0.5 to avoid
older native libraries pulled in by EF SQLite. The initial warning referred to
[GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q).

## Integration Coverage and Remaining Limits

This review is not an exhaustive security or concurrency audit.
SQLite and NSubstitute remain useful for focused tests. The additional container
suite runs real PostgreSQL and Redis. The [layered consumer](../tests/Consumer/README.md) maps each covered
pattern to its skill and tests. Low-level tests retain direct EF access to prepare
and inspect state; sample handlers do not call `SaveChanges`, access `DbSet`,
or modify tracking.

- **Covered with containers:** transaction isolation across scopes, rollback and
  disposal, committed cache state, UTC timestamps, cache expiration/repopulation,
  PostgreSQL upserts/deletes, uniqueness failures and competing inserts, validation,
  cancellation, and database durability when Redis shuts down before commit.
- **Covered in corresponding library tests:** endpoints, binders, byte/path downloads,
  redirects, notifications, exception handling/auditing, authorization composition,
  and the generator cases described above.
- **Still outside this suite:** distributed read/write cache invalidation races,
  retry/reconciliation after failed cache invalidation, database migrations,
  load testing, Native AOT publishing, and arbitrary open generic handler/repository
  registration. Redis is eventually consistent; the outage test does not guarantee
  immediate cache repair. HTTP tests use TestServer and do not validate network/proxy
  disconnect timing. PostgreSQL isolation tests use separate scopes/connections,
  not separately deployed application processes.

Repository tests also cover automatic saving, updates, selective hard deletion,
and exclusion of soft-deleted rows.
