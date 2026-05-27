using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SebastianGuzmanMorla.DDD.Domain.Messaging;
using StackExchange.Redis;

namespace SebastianGuzmanMorla.DDD.Services;

public class CachedHealthCheckService(
    HealthCheckService healthCheckService,
    IConnectionMultiplexer redis,
    IOptions<DddHealthCheckOptions> options,
    ILogger<CachedHealthCheckService> logger,
    JsonSerializerOptions? jsonSerializerOptions = null
) : BackgroundService
{
    private readonly IDatabase _db = redis.GetDatabase();
    private readonly DddHealthCheckOptions _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string lockToken = Guid.NewGuid().ToString();
        TimeSpan interval = TimeSpan.FromSeconds(_settings.CacheIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await _db.LockTakeAsync(_settings.RedisLockKey, lockToken, TimeSpan.FromSeconds(30)))
                {
                    try
                    {
                        HealthReport report = await healthCheckService.CheckHealthAsync(stoppingToken);

                        HealthCheckReportModel model = FromHealthReport(report);

                        string json = jsonSerializerOptions is not null
                            ? JsonSerializer.Serialize(model, typeof(HealthCheckReportModel), jsonSerializerOptions)
                            : JsonSerializer.Serialize(model);

                        await _db.StringSetAsync(_settings.RedisKey, json, TimeSpan.FromMinutes(2));
                    }
                    finally
                    {
                        await _db.LockReleaseAsync(_settings.RedisLockKey, lockToken);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error executing health checks");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private static HealthCheckReportModel FromHealthReport(HealthReport report)
    {
        return new HealthCheckReportModel
        {
            Status = report.Status.ToString(),
            TotalDuration = report.TotalDuration.ToString(),
            Entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new HealthCheckEntryModel
                {
                    Data = e.Value.Data,
                    Description = e.Value.Description,
                    Duration = e.Value.Duration.ToString(),
                    Status = e.Value.Status.ToString(),
                    Tags = e.Value.Tags
                })
        };
    }
}
