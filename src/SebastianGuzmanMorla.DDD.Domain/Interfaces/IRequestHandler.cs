using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace SebastianGuzmanMorla.DDD.Domain.Interfaces;

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : Request<TResponse>
    where TResponse : Response, new()
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken = default);
}
