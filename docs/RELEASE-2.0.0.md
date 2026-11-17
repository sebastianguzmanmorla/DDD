# DDD 2.0.0

Security hardening release for the DDD, DDD.Domain and DDD.Infrastructure packages.

## Migration notes
- Validator and SmartEnum dependencies now require 2.0.0. Dynamic validator consumers must use ValidateWithMessage; cancellation propagates as cancellation.
- CachedRepository reads use database visibility by default. Enable shared caching only for uniformly visible data; it remains eventually consistent.
- Pagination rejects sizes above 100 and offsets above 100,000 even without a registered validator.
- Public cached health responses expose status only; cache failures return 503 without executing live probes. Detailed health requires explicit authorization and a distinct Redis key.
- Source generation reports DDD001 for sensitive properties that cannot be safely redacted.
- Update previously copied audit templates to commit audit records in the same transaction as mutations.
- NuGet publishing no longer places API keys in process arguments.

Targets remain .NET 9 and .NET 10. No production configuration or credentials are changed by this release.
