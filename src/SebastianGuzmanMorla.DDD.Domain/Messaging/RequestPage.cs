namespace SebastianGuzmanMorla.DDD.Domain.Messaging;

public abstract class RequestPage<TResponse> : Request<TResponse>
    where TResponse : Response, new()
{
    private int? _page;

    public int? Page
    {
        get => _page;
        set => _page = value.HasValue && value.Value <= 0 ? 1 : value;
    }

    public int? Size { get; set; }
}
