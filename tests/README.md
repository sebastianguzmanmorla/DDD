# Test Projects

Each library project has a corresponding test project with the same name and
a `.Tests` suffix. Directories, namespaces, and assemblies follow this convention.
Use [Library and Consumer Testing](../.skills/21-library-consumer-testing.md) when
adding cases; it explains which behavior belongs at each verification layer.

| Test project | Coverage | Cases per framework |
| --- | --- | ---: |
| [SebastianGuzmanMorla.DDD.Tests](SebastianGuzmanMorla.DDD.Tests/SebastianGuzmanMorla.DDD.Tests.csproj) | Hashing, pagination, HTTP endpoints, binders, file responses, and authorization policies. | 60 |
| [SebastianGuzmanMorla.DDD.Domain.Tests](SebastianGuzmanMorla.DDD.Domain.Tests/SebastianGuzmanMorla.DDD.Domain.Tests.csproj) | Consumer domain validation, entities, cloning, and serialization. | 9 |
| [SebastianGuzmanMorla.DDD.Infrastructure.Tests](SebastianGuzmanMorla.DDD.Infrastructure.Tests/SebastianGuzmanMorla.DDD.Infrastructure.Tests.csproj) | Repositories, caching, UoW, consumer workflows, cancellation, notifications, and exception/audit middleware. | 47 |
| [SebastianGuzmanMorla.DDD.Generator.Tests](SebastianGuzmanMorla.DDD.Generator.Tests/SebastianGuzmanMorla.DDD.Generator.Tests.csproj) | Generated registrations and mappings, nested/global namespaces, partial types, and generic notifications/maps. | 8 |
| [SebastianGuzmanMorla.DDD.Domain.Generator.Tests](SebastianGuzmanMorla.DDD.Domain.Generator.Tests/SebastianGuzmanMorla.DDD.Domain.Generator.Tests.csproj) | Redaction compilation/execution, generic/nested/internal types, duplicate names, and generated base cleanup. | 13 |
| [Consumer.Integration.Tests](Consumer/Consumer.Integration.Tests/Consumer.Integration.Tests.csproj) | Real PostgreSQL and Redis through Testcontainers. | 12 |

Total: **149 cases per framework**, including 12 container cases, on .NET 9 and .NET 10.
The NuGet package smoke test runs separately on both frameworks.

`Shared/` contains fixtures linked as source code by projects that need them;
no test project depends on another test project.
`TestProjects.props` shares xUnit and framework configuration;
`SqliteTests.props` is imported only where SQLite is needed.

The projects under [Consumer/](Consumer/README.md) form the layered consumer
sample based on the skills. They remain supporting projects and are not published.

## Running Tests

```bash
# Entire solution (requires Docker)
dotnet test SebastianGuzmanMorla.DDD.slnx -c Release

# Fast suite without Docker
make test-unit

# PostgreSQL/Redis integration only
make test-integration

# Install locally built packages into a temporary consumer (requires Python 3)
make test-package
```

Use `-f net9.0` or `-f net10.0` with `dotnet test` to select one framework.
Both SDK/runtime targets must be available for the full suite; SDK 10 compiles the
Roslyn 5 generators. Container tests fail explicitly when Docker is unavailable.
