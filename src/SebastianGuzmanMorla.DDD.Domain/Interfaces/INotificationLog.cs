using System.Text.Json;
using SebastianGuzmanMorla.DDD.Domain.Entities;

namespace SebastianGuzmanMorla.DDD.Domain.Interfaces;

public interface INotificationLog : INotification
{
    public LogType Type { get; }
    
    public string Message { get; }
    
    public string? ReferenceType { get; }
    
    Log ToLogEntity(JsonSerializerOptions jsonSerializerOptions, Guid? logRequestId = null);
}
