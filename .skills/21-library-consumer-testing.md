# 21. Library and Consumer Testing

Use this guide when adding library regressions or verifying that an application
consumes the DDD packages correctly. Select the tests relevant to the changed behavior.

## Project Boundaries

- Name focused test projects after the project they exercise, with a `.Tests`
  suffix. Keep test names, comments, and documentation in English.
- Use separate Contracts, Domain, Infrastructure, and Application projects in a
  consumer fixture so references, generators, and DI cross real assembly boundaries.
- Keep domain rules and private state in entities, syntax checks in contract
  validators, and repository-dependent rules in domain validators. Application
  handlers mutate through repositories and UoW. Fixtures may inspect EF directly
  to seed/assert state or flush uncommitted SQL for isolation tests.
- Use xUnit and NSubstitute for focused tests. Stub localization through
  `ValidatorTestBase`; keep shared helpers independent of test assemblies.

## Choose the Verification Layer

| Change | Observable behavior to verify |
| --- | --- |
| Validation/domain | Invalid inputs, domain invariants, rule paths/messages, short-circuiting, repository calls, cancellation. |
| Persistence/cache | Commit/rollback/disposal across scopes, soft deletion, UTC values, cache isolation/invalidation, provider-specific upsert, uniqueness, expiration, and failure recovery. |
| Handlers | Cancellation propagation, safe error responses, audit hooks, notifications only on success, no repeated delivery when reused. |
| HTTP | TestServer requests through actual mappings: verbs, query/body binding, binder failures, status codes, redirects, file bytes/path downloads, and safe file errors. |
| Authorization | Compose authenticated policies with valid, missing, unknown, repeated, and custom-type claims. |
| Generators | Compile emitted code and execute it; verify supported type shapes, DI lifetimes, redaction, and mapping metadata. |
| Packaging | Install locally packed libraries into an isolated consumer without explicit analyzer project references. Verify bundled generators load and work. |

## Testcontainers

Use real PostgreSQL and Redis for provider/cache integration cases. SQLite and
mock Redis calls remain useful for fast regressions, but do not establish
PostgreSQL SQL behavior or real cache semantics.

- Pin image/package versions, use dynamically allocated ports, and create isolated
  containers through an asynchronous fixture. Dispose resources on failure as well
  as success. Do not connect integration tests to a shared or production database.
- Use unique IDs/names or isolated databases so tests do not depend on order.
  Use separate scopes/connections to verify visibility and concurrent writes.
- Mark container tests `[Trait("Category", "Integration")]`. Fail explicitly when
  Docker is unavailable; keep a documented filter for the fast suite.
- Isolate outage scenarios from other tests. Distinguish database durability from
  cache repair: a swallowed post-commit callback failure does not guarantee fresh Redis data.

## Commands in This Repository

From the DDD source checkout, with SDK 10 and .NET 9/10 runtimes installed:

```bash
make test-unit          # Focused suite; no Docker required
make test-integration   # Consumer with PostgreSQL and Redis; Docker required
make test               # Both suites, both target frameworks
make test-package       # Local NuGet installation; Python 3 and NuGet access required
```

The executable examples are under `tests/Consumer/`; commands and project counts
are maintained in `tests/README.md`. These repository test files are not shipped
inside the library packages. When applying this skill in another repository,
adapt the fixture and commands to that consumer rather than assuming they exist.

Run checks affected by a change and record frameworks, failures/skips, and material
limits. Do not infer Native AOT support, migration correctness, distributed cache
consistency, or real proxy/disconnect behavior from the focused suite alone.
