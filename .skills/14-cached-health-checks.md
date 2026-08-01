# 14. Cached Health Checks

To optimize performance and protect critical database systems from high-frequency status requests, health check results are cached in Redis.

---

## A. Background Runner Registration (`Program.cs`)
`CachedHealthCheckService` runs system checks periodically, updates cached status in Redis under a distributed lock, and formats output into a `HealthCheckReportModel`.

```csharp
using Microsoft.Extensions.Options;
using SebastianGuzmanMorla.DDD.Infrastructure.Services;
using SebastianGuzmanMorla.DDD.Infrastructure.Options;

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

Expose endpoint in `Program.cs` before middlewares:

```csharp
using SebastianGuzmanMorla.DDD.Infrastructure.Extensions;

app.MapCachedHealthChecks("/health");
```

### HTTP Status Code Mapping
* `"Healthy"` -> `200 OK`
* `"Degraded"` -> `429 TooManyRequests`
* `"Unhealthy"` / Other -> `503 ServiceUnavailable`
