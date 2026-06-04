using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace SebastianGuzmanMorla.DDD.Extensions;

public static class RequestExtensions
{
    public static Task<TResponse> Handle<TRequest, TResponse>(this TRequest request, IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
        where TRequest : Request<TResponse>
        where TResponse : Response, new()
    {
        return serviceProvider
            .GetRequiredService<IRequestHandler<TRequest, TResponse>>()
            .Handle(request, cancellationToken);
    }
}
