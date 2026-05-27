namespace SebastianGuzmanMorla.DDD.Domain.Entities;

public sealed class LogRequest : Entity
{
    public string? Context { get; set; }

    public required string Type { get; set; }

    public required string Request { get; set; }
}
