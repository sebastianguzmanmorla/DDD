namespace SebastianGuzmanMorla.DDD.Domain.Messaging;

public abstract class RequestPage<TResponse> : Request<TResponse>
    where TResponse : Response, new()
{
    public int? Page { get; set; }

    public int? Size { get; set; }
}
