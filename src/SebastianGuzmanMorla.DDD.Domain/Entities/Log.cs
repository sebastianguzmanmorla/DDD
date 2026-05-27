namespace SebastianGuzmanMorla.DDD.Domain.Entities;

public sealed class Log : Entity
{
    public Guid? LogRequestId { get; set; }

    public LogType Type { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? ReferenceType { get; set; }

    public Guid? ReferenceId { get; set; }

    public string? ReferenceData { get; set; }
}
