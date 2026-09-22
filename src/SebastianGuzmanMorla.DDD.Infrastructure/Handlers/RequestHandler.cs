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
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await HandleCore(request, cancellationToken);
        }
        finally
        {
            Notifications.Clear();
        }
    }

    private async Task<TResponse> HandleCore(TRequest request, CancellationToken cancellationToken)
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
                        Message = "Validation errors",
                        Errors = validationResult.Errors
                    };
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await OnException(request, ex, cancellationToken);

                response = new TResponse
                {
                    Status = HttpStatusCode.InternalServerError,
                    Message = "Internal server error"
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await OnException(request, ex, cancellationToken);

                response = new TResponse
                {
                    Status = HttpStatusCode.InternalServerError,
                    Message = "Internal server error"
                };
            }
        }

        await OnAfterExecute(request, response, cancellationToken);

        if ((int)response.Status is < 200 or >= 300) return response;

        foreach (INotification notification in Notifications)
        {
            try
            {
                await notification.Handle(ServiceProvider, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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
