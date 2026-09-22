using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SebastianGuzmanMorla.DDD.Domain.Entities;
using SebastianGuzmanMorla.DDD.Domain.Messaging;
using SebastianGuzmanMorla.DDD.Infrastructure.Mappings;
using SebastianGuzmanMorla.DDD.Infrastructure.Middleware;

namespace SebastianGuzmanMorla.DDD.Infrastructure.Tests.Middleware;

public sealed class ExceptionHandlerMiddlewareTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientCancellation_Returns499WithoutErrorBody(bool taskCancellation)
    {
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        DefaultHttpContext context = Context(services);
        context.RequestAborted = cancellation.Token;
        Exception error = taskCancellation ? new TaskCanceledException() : new OperationCanceledException(cancellation.Token);
        await Invoke(context, services, error);
        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task BadRequest_Returns400WithValidationMessage()
    {
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        DefaultHttpContext context = Context(services);
        await Invoke(context, services, new BadHttpRequestException("Invalid payload"));
        Response body = await ReadResponse(context);
        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, body.Status);
        Assert.Equal("Invalid payload", body.Message);
    }

    [Fact]
    public async Task UnexpectedError_PersistsAuditRecordAndReturnsOnlySafeMessageAndLogId()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using ServiceProvider services = new ServiceCollection().AddDbContext<AuditContext>(o => o.UseSqlite(connection)).BuildServiceProvider();
        await using (AsyncServiceScope setup = services.CreateAsyncScope())
            await setup.ServiceProvider.GetRequiredService<AuditContext>().Database.EnsureCreatedAsync();
        DefaultHttpContext context = Context(services);
        await Invoke(context, services, new InvalidOperationException("private database details"));
        Response body = await ReadResponse(context);
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("Internal server error", body.Message);
        Assert.NotNull(body.LogId);
        await using AsyncServiceScope verification = services.CreateAsyncScope();
        Log log = await verification.ServiceProvider.GetRequiredService<AuditContext>().Set<Log>().SingleAsync();
        Assert.Equal(body.LogId, log.Id);
        Assert.Contains("private database details", log.Message);
    }

    [Fact]
    public async Task AuditFailure_StillReturnsSafe500WithoutLogId()
    {
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        DefaultHttpContext context = Context(services);
        await Invoke(context, services, new InvalidOperationException("private details"));
        Response body = await ReadResponse(context);
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Null(body.LogId);
        Assert.Equal("Internal server error", body.Message);
    }

    [Fact]
    public async Task CancellationWithoutClientAbort_IsHandledAsServerError()
    {
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        DefaultHttpContext context = Context(services);
        await Invoke(context, services, new TaskCanceledException("upstream timed out"));
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("Internal server error", (await ReadResponse(context)).Message);
    }

    [Fact]
    public async Task StartedResponse_RethrowsOriginalErrorWithoutAppendingJson()
    {
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        DefaultHttpContext context = Context(services);
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        var error = new InvalidOperationException("stream failed");
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(context, services, error)));
        Assert.Equal(0, context.Response.Body.Length);
    }

    private static DefaultHttpContext Context(IServiceProvider services) => new()
    { RequestServices = services, Response = { Body = new MemoryStream() } };

    private static Task Invoke(HttpContext context, IServiceProvider services, Exception error) =>
        new ExceptionHandlerMiddleware<AuditContext>(_ => throw error)
            .InvokeAsync(context, NullLogger<ExceptionHandlerMiddleware<AuditContext>>.Instance, services);

    private static async Task<Response> ReadResponse(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return (await JsonSerializer.DeserializeAsync<Response>(context.Response.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    public sealed class AuditContext(DbContextOptions<AuditContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyConfiguration(new LogMap());
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }
}
