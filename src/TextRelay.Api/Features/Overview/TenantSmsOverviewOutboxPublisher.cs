using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sms.Infrastructure.Messaging;

public sealed class TenantSmsOverviewOutboxPublisher(
    ITenantSmsOverviewOutbox outbox,
    ITenantSmsOverviewEventPublisher eventPublisher,
    ILogger<TenantSmsOverviewOutboxPublisher> logger) : PollingBackgroundService(PollingInterval, logger)
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMinutes(5);

    protected override string FailureMessage => "Failed to publish tenant SMS overview outbox batch.";

    protected override async Task ExecuteIterationAsync(CancellationToken stoppingToken) =>
        await PublishBatchAsync(stoppingToken);

    public async Task<int> PublishBatchAsync(CancellationToken cancellationToken = default)
    {
        var published = 0;
        foreach (var item in await outbox.GetPendingAsync(cancellationToken))
        {
            await eventPublisher.PublishAsync(item, cancellationToken);
            await outbox.MarkPublishedAsync(item.EventId, cancellationToken);
            published++;
        }

        return published;
    }
}
