using System.Net;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.DDD.Domain.Messaging;
using SebastianGuzmanMorla.DDD.Infrastructure.Handlers;
using SebastianGuzmanMorla.Validator;
using SebastianGuzmanMorla.Validator.Interfaces;

namespace SebastianGuzmanMorla.DDD.Infrastructure.Tests.Handlers;

public sealed class RequestHandlerTests
{
    [Fact]
    public async Task ReusedHandler_DeliversEachNotificationOnlyOnce()
    {
        await using ServiceProvider services = Services();
        var notification = new ProbeNotification();
        var handler = new ProbeHandler(services, notification);
        await handler.Handle(new ProbeRequest());
        await handler.Handle(new ProbeRequest());
        Assert.Equal(2, notification.Calls);
        Assert.Equal(2, handler.AfterCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedExecution_DiscardsNotificationsAndDoesNotLeakExceptionDetails(bool throws)
    {
        await using ServiceProvider services = Services();
        var notification = new ProbeNotification();
        var handler = new ProbeHandler(services, notification)
        {
            ExecuteAction = _ => throws
                ? throw new InvalidOperationException("database password=private")
                : Task.FromResult(new Response { Status = HttpStatusCode.Conflict })
        };
        Response response = await handler.Handle(new ProbeRequest());
        Assert.Equal(throws ? HttpStatusCode.InternalServerError : HttpStatusCode.Conflict, response.Status);
        Assert.DoesNotContain("private", response.Message ?? "");
        Assert.Equal(0, notification.Calls);
        Assert.Equal(1, handler.AfterCalls);
        Assert.Equal(throws, handler.Error is not null);
        handler.ExecuteAction = _ => Task.FromResult(new Response());
        await handler.Handle(new ProbeRequest());
        Assert.Equal(1, notification.Calls);
    }

    [Theory]
    [InlineData("validation")]
    [InlineData("execution")]
    [InlineData("notification")]
    public async Task Cancellation_PropagatesAndDoesNotInvokeExceptionHook(string stage)
    {
        using var cancellation = new CancellationTokenSource();
        IValidator<ProbeRequest> validator = Substitute.For<IValidator<ProbeRequest>>();
        validator.Validate(Arg.Any<ProbeRequest>(), Arg.Any<IServiceProvider>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (stage == "validation") { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }
                return Task.FromResult(new ValidationResult());
            });
        await using ServiceProvider services = Services(validator);
        var notification = new ProbeNotification { Action = token =>
        {
            if (stage == "notification") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            return Task.CompletedTask;
        }};
        var handler = new ProbeHandler(services, notification) { ExecuteAction = token =>
        {
            if (stage == "execution") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            return Task.FromResult(new Response());
        }};
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.Handle(new ProbeRequest(), cancellation.Token));
        Assert.Null(handler.Error);
        Assert.Equal(stage == "notification" ? 1 : 0, notification.Calls);
    }

    [Fact]
    public async Task NotificationFailure_DoesNotUndoSuccessfulResponseOrBlockOtherNotifications()
    {
        await using ServiceProvider services = Services();
        var failing = new ProbeNotification { Action = _ => throw new InvalidOperationException("notification unavailable") };
        var healthy = new ProbeNotification();
        var handler = new ProbeHandler(services, failing, healthy);
        Assert.Equal(HttpStatusCode.OK, (await handler.Handle(new ProbeRequest())).Status);
        Assert.Equal(1, failing.Calls);
        Assert.Equal(1, healthy.Calls);
    }

    [Fact]
    public async Task ValidationException_ReturnsSafeErrorWithoutExecutingBusinessOperation()
    {
        IValidator<ProbeRequest> validator = Substitute.For<IValidator<ProbeRequest>>();
        validator.Validate(Arg.Any<ProbeRequest>(), Arg.Any<IServiceProvider>(), Arg.Any<CancellationToken>())
            .Returns<Task<ValidationResult>>(_ => throw new InvalidOperationException("private credentials"));
        await using ServiceProvider services = Services(validator);
        var handler = new ProbeHandler(services);
        Response response = await handler.Handle(new ProbeRequest());
        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        Assert.Equal("Internal server error", response.Message);
        Assert.Equal(0, handler.ExecuteCalls);
        Assert.IsType<InvalidOperationException>(handler.Error);
    }

    private static ServiceProvider Services(IValidator<ProbeRequest>? validator = null)
    {
        IServiceCollection services = new ServiceCollection().AddSingleton(Substitute.For<IUnitOfWork<TestDbContext>>());
        if (validator is not null) services.AddSingleton(validator);
        return services.BuildServiceProvider();
    }

    public sealed class ProbeRequest : Request<Response>
    {
        public override void ClearSensitiveProperties() { }
    }

    private sealed class ProbeHandler(IServiceProvider services, params ProbeNotification[] notifications)
        : RequestHandler<TestDbContext, ProbeRequest, Response>(services)
    {
        public Func<CancellationToken, Task<Response>> ExecuteAction { get; set; } = _ => Task.FromResult(new Response());
        public Exception? Error { get; private set; }
        public int AfterCalls { get; private set; }
        public int ExecuteCalls { get; private set; }
        protected override Task<Response> Execute(ProbeRequest request, CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            foreach (ProbeNotification notification in notifications) AddNotification(notification);
            return ExecuteAction(cancellationToken);
        }
        protected override Task OnException(ProbeRequest request, Exception exception, CancellationToken cancellationToken = default)
        { Error = exception; return Task.CompletedTask; }
        protected override Task OnAfterExecute(ProbeRequest request, Response response, CancellationToken cancellationToken = default)
        { AfterCalls++; return Task.CompletedTask; }
    }

    private sealed class ProbeNotification : INotification
    {
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public int Calls { get; private set; }
        public Func<CancellationToken, Task> Action { get; init; } = _ => Task.CompletedTask;
        public Task Handle(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        { Calls++; return Action(cancellationToken); }
    }
}
