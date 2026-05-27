using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Entities;
using SebastianGuzmanMorla.DDD.Domain.Extensions;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;

namespace SebastianGuzmanMorla.DDD.Domain.Notifications;

public sealed class NotificationLog<TData> : INotificationLog
{
    public Guid? ReferenceId { get; init; }

    public TData? ReferenceData { get; init; }
    public LogType Type { get; init; }

    public string? ReferenceType { get; init; } = typeof(TData).GetFormattedName();
    
    public DateTime Timestamp { get; } = DateTime.UtcNow;

    public string Message { get; init; } = string.Empty;

    public Task Handle(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        INotificationHandler<NotificationLog<TData>> handler = serviceProvider.GetRequiredService<INotificationHandler<NotificationLog<TData>>>();

        return handler.Handle(this, cancellationToken);
    }

    public Log ToLogEntity(JsonSerializerOptions jsonSerializerOptions, Guid? logRequestId = null)
    {
        return new Log
        {
            Id = Guid.CreateVersion7(),
            LogRequestId = logRequestId,
            Type = Type,
            Message = Message,
            ReferenceType = ReferenceType,
            ReferenceId = ReferenceId,
            ReferenceData = ReferenceData is not null
                ? JsonSerializer.Serialize(ReferenceData, jsonSerializerOptions)
                : null,
            UpdatedAt = Timestamp
        };
    }
}
