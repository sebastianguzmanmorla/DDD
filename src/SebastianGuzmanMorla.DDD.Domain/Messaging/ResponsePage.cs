namespace SebastianGuzmanMorla.DDD.Domain.Messaging;

public abstract class ResponsePage<TEntity> : Response
    where TEntity : notnull
{
    public int? Page { get; set; }
    public int? Pages { get; set; }
    public int? Size { get; set; }
    public int? Total { get; set; }

    public List<TEntity>? Items { get; init; }
}
