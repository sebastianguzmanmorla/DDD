using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.DDD.Domain.Messaging;
using SebastianGuzmanMorla.Validator;
using SebastianGuzmanMorla.Validator.Interfaces;

namespace SebastianGuzmanMorla.DDD.Infrastructure.Handlers;

public abstract class RequestHandler<TContext, TRequest, TResponse>(
    IServiceProvider serviceProvider
) : IRequestHandler<TRequest, TResponse>
    where TContext : DbContext
    where TRequest : Request<TResponse>
    where TResponse : Response, new()
{
    protected readonly IServiceProvider ServiceProvider = serviceProvider;
    protected readonly IUnitOfWork<TContext> UnitOfWork = serviceProvider.GetRequiredService<IUnitOfWork<TContext>>();
    protected readonly List<INotification> Notifications = [];

    public async Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken = default)
    {
        TResponse? response = null;

        IValidator<TRequest>? validator = ServiceProvider.GetService<IValidator<TRequest>>();

        if (validator is not null)
        {
            try
            {
                ValidationResult validationResult =
                    await validator.Validate(request, ServiceProvider, cancellationToken);

                if (!validationResult.IsValid)
                {
                    response = new TResponse
                    {
                        Status = HttpStatusCode.BadRequest,
                        Message = "Errores al validar",
                        Errors = validationResult.Errors
                    };
                }
            }
            catch (Exception ex)
            {
                await OnException(request, ex, cancellationToken);

                response = new TResponse
                {
                    Status = HttpStatusCode.InternalServerError,
                    Message = ex.Message
                };
            }
        }

        if (response is null)
        {
            try
            {
                await using (UnitOfWork)
                {
                    response = await Execute(request, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                await OnException(request, ex, cancellationToken);

                response = new TResponse
                {
                    Status = HttpStatusCode.InternalServerError,
                    Message = ex.Message
                };
            }
        }

        await OnAfterExecute(request, response, cancellationToken);

        foreach (INotification notification in Notifications)
        {
            try
            {
                await notification.Handle(ServiceProvider, cancellationToken);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        return response;
    }

    protected abstract Task<TResponse> Execute(TRequest request, CancellationToken cancellationToken = default);

    protected virtual Task OnException(TRequest request, Exception exception, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    protected virtual Task OnAfterExecute(TRequest request, TResponse response, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    protected void AddNotification(INotification notification)
    {
        Notifications.Add(notification);
    }
}
