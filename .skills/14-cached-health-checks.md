# 14. Cached Health Checks

To optimize performance and protect critical database systems from high-frequency status requests, health check results are cached in Redis.

---

## A. Background Runner Registration (`Program.cs`)
`CachedHealthCheckService` runs system checks periodically, updates cached status in Redis under a distributed lock, and formats output into a `HealthCheckReportModel`.

```csharp
using Microsoft.Extensions.Options;
using SebastianGuzmanMorla.DDD.Infrastructure.Services;
using SebastianGuzmanMorla.DDD.Domain.Options;

builder.Services.AddOptions<CachedHealthCheckOptions>()
    .Configure<IOptions<HealthCheckSettings>>((options, settings) =>
    {
        options.RedisKey = "MyProject:health";
        options.RedisLockKey = "MyProject:locks:health";
        options.CacheIntervalSeconds = settings.Value.CacheIntervalSeconds;
    });

builder.Services.AddSingleton<CachedHealthCheckService>();
builder.Services.AddHostedService(serviceProvider =>
    serviceProvider.GetRequiredService<CachedHealthCheckService>());
```

---

## B. Endpoint Mapping (`Program.cs`)

Map a status-only readiness endpoint in `Program.cs`. Assign unique Redis report and lock keys for each application and environment that shares a Redis database.

```csharp
using SebastianGuzmanMorla.DDD.Infrastructure.Extensions;

app.MapCachedHealthChecks("/health");
// Optional, with a named policy that grants access to operators.
app.MapDetailedCachedHealthChecks("HealthDiagnostics", "/health/details");
```

### HTTP Status Code Mapping
* `"Healthy"` -> `200 OK`
* `"Degraded"` -> `429 TooManyRequests`
* `"Unhealthy"` or `"Unavailable"` -> `503 ServiceUnavailable`

A missing, invalid, or unavailable Redis report returns `"Unavailable"`. Both routes read the cache without running probes. The detailed route requires an authenticated operator, a named policy and a non-default Redis report key.
