namespace SebastianGuzmanMorla.DDD.Interfaces;

public interface IRequestBinder<TRequest, TErrorResponse>
{
    Task<(TRequest?, TErrorResponse?)> BindAsync(CancellationToken cancellationToken = default);
}
