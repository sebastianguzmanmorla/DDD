namespace SebastianGuzmanMorla.DDD.Domain.Interfaces;

public interface INotification
{
    public DateTime Timestamp { get; }

    Task Handle(IServiceProvider serviceProvider, CancellationToken cancellationToken = default);
}
