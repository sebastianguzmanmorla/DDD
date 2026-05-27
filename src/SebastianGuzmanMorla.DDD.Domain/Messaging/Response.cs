using System.Net;

namespace SebastianGuzmanMorla.DDD.Domain.Messaging;

public class Response
{
    public virtual Guid? LogId { get; set; }

    public virtual DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public virtual HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public virtual string Message { get; set; } = "Ok";

    public virtual Dictionary<string, List<string>>? Errors { get; set; }
}

public class Response<TData> : Response
{
    public virtual TData? Data { get; set; }
}
