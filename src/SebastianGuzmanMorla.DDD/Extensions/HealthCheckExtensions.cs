using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SebastianGuzmanMorla.DDD.Domain.Models;
using SebastianGuzmanMorla.DDD.Domain.Options;
using SebastianGuzmanMorla.DDD.Services;
using StackExchange.Redis;

namespace SebastianGuzmanMorla.DDD.Extensions;

public static class HealthCheckExtensions
{
    public static IEndpointConventionBuilder MapCachedHealthChecks(this IEndpointRouteBuilder endpoints, string pattern = "/health")
    {
        return endpoints.MapHealthChecks(pattern, new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = async (context, _) =>
            {
                string? json = null;
                
                try
                {
                    IConnectionMultiplexer redis = context.RequestServices.GetRequiredService<IConnectionMultiplexer>();
                    IDatabase db = redis.GetDatabase();

                    CachedHealthCheckOptions healthCheckOptions = context.RequestServices.GetRequiredService<IOptions<CachedHealthCheckOptions>>().Value;
                    RedisValue value = await db.StringGetAsync(healthCheckOptions.RedisKey);
                    json = value.ToString();
                }
                catch (Exception)
                {
                    // ignored
                }

                if (string.IsNullOrEmpty(json))
                {
                    try
                    {
                        HealthCheckService healthCheckService = context.RequestServices.GetRequiredService<HealthCheckService>();
                        HealthReport liveReport = await healthCheckService.CheckHealthAsync(context.RequestAborted);
                        HealthCheckReportModel model = CachedHealthCheckService.FromHealthReport(liveReport);

                        JsonSerializerOptions? jsonOptions = context.RequestServices.GetService<JsonSerializerOptions>();
                        json = jsonOptions is not null
                            ? JsonSerializer.Serialize(model, jsonOptions)
                            : JsonSerializer.Serialize(model);
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                }

                if (string.IsNullOrEmpty(json))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsync("Health check report no disponible en caché o en vivo.");
                    return;
                }

                HealthCheckReportModel? report = JsonSerializer.Deserialize<HealthCheckReportModel>(json);

                context.Response.StatusCode = report?.Status switch
                {
                    "Healthy" => StatusCodes.Status200OK,
                    "Degraded" => StatusCodes.Status429TooManyRequests,
                    _ => StatusCodes.Status503ServiceUnavailable
                };

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(json);
            }
        });
    }
}
