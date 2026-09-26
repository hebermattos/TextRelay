namespace Sms.Infrastructure.Messaging;

public interface ITenantSmsOverviewOutbox
{
    Task<IReadOnlyList<TenantSmsOverviewEvent>> GetPendingAsync(CancellationToken cancellationToken = default);
    Task MarkPublishedAsync(Guid eventId, CancellationToken cancellationToken = default);
}

public interface ITenantSmsOverviewEventPublisher
{
    Task PublishAsync(TenantSmsOverviewEvent item, CancellationToken cancellationToken = default);
}

public interface ITenantSmsOverviewProjection
{
    Task ApplyAsync(TenantSmsOverviewEvent item, CancellationToken cancellationToken = default);
}
