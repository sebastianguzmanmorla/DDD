namespace SebastianGuzmanMorla.DDD.Domain.Messaging;

public abstract class Request<TResponse> : Request where TResponse : Response, new()
{
}

public abstract class Request : ICloneable
{
    public virtual DateTime? Timestamp { get; set; } = DateTime.UtcNow;

    public object Clone()
    {
        return MemberwiseClone();
    }

    public abstract void ClearSensitiveProperties();
}
