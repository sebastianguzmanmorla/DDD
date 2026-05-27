namespace SebastianGuzmanMorla.DDD.Domain.Messaging;

public abstract class ResponseFile : Response
{
    public string? FileName { get; set; }
    public string? FileType { get; set; } = "application/octet-stream";
}
