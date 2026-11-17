using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SebastianGuzmanMorla.DDD.Domain.Options;
using StackExchange.Redis;

namespace SebastianGuzmanMorla.DDD.Infrastructure.Extensions;

public static class HealthCheckExtensions
{
    private static readonly string DefaultReportKey = new CachedHealthCheckOptions().RedisKey;

    public static IEndpointConventionBuilder MapCachedHealthChecks(this IEndpointRouteBuilder endpoints, string pattern = "/health")
        => MapCachedHealthChecks(endpoints, pattern, includeDetails: false);

    public static IEndpointConventionBuilder MapDetailedCachedHealthChecks(
        this IEndpointRouteBuilder endpoints, string authorizationPolicy, string pattern = "/health/details")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationPolicy);
        IEndpointConventionBuilder builder = MapCachedHealthChecks(endpoints, pattern, includeDetails: true)
            .RequireAuthorization(authorizationPolicy)
            .RequireAuthorization(policy => policy.RequireAuthenticatedUser());
        builder.Finally(endpoint =>
        {
            if (endpoint.Metadata.OfType<IAllowAnonymous>().Any())
            {
                throw new InvalidOperationException("Detailed cached health checks require authorization.");
            }
        });
        return builder;
    }

    private static IEndpointConventionBuilder MapCachedHealthChecks(
        IEndpointRouteBuilder endpoints, string pattern, bool includeDetails)
    {
        return endpoints.MapHealthChecks(pattern, new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = async (context, _) =>
            {
                string? json = null;
                string? status = null;

                try
                {
                    CachedHealthCheckOptions healthCheckOptions = context.RequestServices.GetRequiredService<IOptions<CachedHealthCheckOptions>>().Value;
                    if (!includeDetails || !string.Equals(healthCheckOptions.RedisKey, DefaultReportKey, StringComparison.Ordinal))
                    {
                        IConnectionMultiplexer redis = context.RequestServices.GetRequiredService<IConnectionMultiplexer>();
                        RedisValue value = await redis.GetDatabase().StringGetAsync(healthCheckOptions.RedisKey);
                        json = value.ToString();
                    }

                    if (!string.IsNullOrEmpty(json))
                    {
                        using JsonDocument report = JsonDocument.Parse(json);
                        JsonElement root = report.RootElement;

                        if (root.ValueKind == JsonValueKind.Object &&
                            (root.TryGetProperty("status", out JsonElement statusValue) ||
                             root.TryGetProperty("Status", out statusValue)) &&
                            statusValue.ValueKind == JsonValueKind.String)
                        {
                            status = statusValue.GetString();
                        }
                    }
                }
                catch (Exception ex) when (ex is RedisException or RedisCommandException or TimeoutException or
                    JsonException or InvalidOperationException)
                {
                    // Cache failures are unavailable readiness, not a signal to run probes on request threads.
                }

                context.Response.StatusCode = status switch
                {
                    "Healthy" => StatusCodes.Status200OK,
                    "Degraded" => StatusCodes.Status429TooManyRequests,
                    _ => StatusCodes.Status503ServiceUnavailable
                };

                context.Response.ContentType = "application/json";
                if (includeDetails && status is ("Healthy" or "Degraded" or "Unhealthy"))
                {
                    await context.Response.WriteAsync(json!, context.RequestAborted);
                    return;
                }

                string body = status switch
                {
                    "Healthy" => """{"status":"Healthy"}""",
                    "Degraded" => """{"status":"Degraded"}""",
                    "Unhealthy" => """{"status":"Unhealthy"}""",
                    _ => """{"status":"Unavailable"}"""
                };
                await context.Response.WriteAsync(body, context.RequestAborted);
            }
        });
    }
}
