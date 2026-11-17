using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SebastianGuzmanMorla.DDD.Domain.Options;
using SebastianGuzmanMorla.DDD.Infrastructure.Extensions;
using StackExchange.Redis;

namespace SebastianGuzmanMorla.DDD.Infrastructure.Tests.Health;

public class CachedHealthEndpointTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(null, typeof(RedisConnectionException))]
    [InlineData(null, typeof(TimeoutException))]
    [InlineData("{", null)]
    [InlineData("""{"status":[],"entries":{}}""", null)]
    public async Task UnavailableReportsNeverRunRequestTimeChecks(string? value, Type? redisError)
    {
        Exception? error = redisError == typeof(RedisConnectionException)
            ? new RedisConnectionException(ConnectionFailureType.UnableToConnect, "unavailable")
            : redisError == typeof(TimeoutException) ? new TimeoutException() : null;
        await using var fixture = new HealthFixture(value, error);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => fixture.Read("/health")));

        Assert.All(responses, response =>
        {
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
            Assert.Equal("""{"status":"Unavailable"}""", response.Body);
        });
        Assert.Equal(0, fixture.LiveChecks);
    }

    [Theory]
    [InlineData("Healthy", 200)]
    [InlineData("Degraded", 429)]
    [InlineData("Unhealthy", 503)]
    public async Task PublicResponseIncludesOnlyAggregateStatus(string status, int expectedStatus)
    {
        string report = JsonSerializer.Serialize(new { status, entries = new { @internal = "fixture-data" } });
        await using var fixture = new HealthFixture(report);

        var response = await fixture.Read("/health");

        Assert.Equal(expectedStatus, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(response.Body);
        Assert.Equal(status, Assert.Single(body.RootElement.EnumerateObject()).Value.GetString());
        Assert.Equal(0, fixture.LiveChecks);
    }

    [Fact]
    public async Task DetailedRouteRequiresAuthenticationAndTheNamedPolicy()
    {
        const string report = """{"status":"Healthy","entries":{"operator-check":{"data":{}}}}""";
        await using var fixture = new HealthFixture(report);
        RouteEndpoint endpoint = fixture.Resolve("/health/details");
        var declaredPolicies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        Assert.Contains(declaredPolicies, policy => policy.Policy == "Operators");
        var provider = fixture.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var combined = await AuthorizationPolicy.CombineAsync(provider, declaredPolicies,
            endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>());
        Assert.NotNull(combined);
        Assert.Contains(combined.Requirements, requirement => requirement is
            Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);

        var response = await fixture.Read("/health/details");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(report, response.Body);
        Assert.Equal(0, fixture.LiveChecks);
    }

    [Fact]
    public async Task DetailedRouteRejectsAnonymousMetadataInheritedFromGroup()
    {
        await using var fixture = new HealthFixture(null, mapAnonymousGroup: true);

        Assert.Throws<InvalidOperationException>(() => fixture.Resolve("/public/health/details"));
    }

    [Fact]
    public async Task DetailedRouteDoesNotReadTheSharedDefaultKey()
    {
        const string report = """{"status":"Healthy","entries":{"fixture-check":{"data":{}}}}""";
        await using var fixture = new HealthFixture(report, useDefaultKey: true);

        var response = await fixture.Read("/health/details");

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Equal("""{"status":"Unavailable"}""", response.Body);
        Assert.Equal(0, fixture.LiveChecks);
    }

    private sealed class HealthFixture : IAsyncDisposable
    {
        private readonly TestEndpointBuilder _builder;
        private int _liveChecks;
        public ServiceProvider Services { get; }
        public int LiveChecks => _liveChecks;

        public HealthFixture(string? value, Exception? redisError = null, bool mapAnonymousGroup = false, bool useDefaultKey = false)
        {
            var cache = Substitute.For<IDatabase>();
            cache.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(_ => redisError is not null
                ? Task.FromException<RedisValue>(redisError)
                : Task.FromResult((RedisValue)value));
            var redis = Substitute.For<IConnectionMultiplexer>();
            redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(cache);

            var services = new ServiceCollection();
            services.AddLogging().AddRouting().AddSingleton(redis);
            if (!useDefaultKey)
            {
                services.Configure<CachedHealthCheckOptions>(options => options.RedisKey = "fixture:health");
            }
            services.AddAuthorization(options => options.AddPolicy("Operators", policy => policy.RequireRole("Operator")));
            services.AddHealthChecks().AddCheck("fixture", () =>
            {
                Interlocked.Increment(ref _liveChecks);
                return HealthCheckResult.Healthy();
            });
            Services = services.BuildServiceProvider();
            _builder = new TestEndpointBuilder(Services);
            _builder.MapCachedHealthChecks();
            _builder.MapDetailedCachedHealthChecks("Operators");
            if (mapAnonymousGroup)
            {
                _builder.MapGroup("/public").AllowAnonymous().MapDetailedCachedHealthChecks("Operators");
            }
        }

        public RouteEndpoint Resolve(string pattern) => _builder.DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == pattern);

        public async Task<(int StatusCode, string Body)> Read(string pattern)
        {
            await using var body = new MemoryStream();
            var context = new DefaultHttpContext { RequestServices = Services };
            context.Response.Body = body;
            await Resolve(pattern).RequestDelegate!(context);
            body.Position = 0;
            using var reader = new StreamReader(body);
            return (context.Response.StatusCode, await reader.ReadToEndAsync());
        }

        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }

    private sealed class TestEndpointBuilder(IServiceProvider services) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider => services;
        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(services);
    }
}
