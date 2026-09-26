using MassTransit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sms.Infrastructure.Messaging;

public sealed class SmsQueuePublisherWorker(
    ISmsQueuePublishSource source,
    IBus bus,
    ILogger<SmsQueuePublisherWorker> logger) : PollingBackgroundService(PollingInterval, logger)
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMinutes(5);

    protected override string FailureMessage => "Failed to publish SMS messages to the send queue.";

    protected override async Task ExecuteIterationAsync(CancellationToken stoppingToken) =>
        await PublishBatchAsync(stoppingToken);

    internal async Task<int> PublishBatchAsync(CancellationToken cancellationToken = default)
    {
        var rows = await source.GetPendingAsync(cancellationToken);
        var published = 0;

        foreach (var row in rows)
        {
            if (!await source.TryClaimAsync(row, cancellationToken))
                continue;

            try
            {
                await bus.Publish(
                    new SmsSendEvent(Guid.NewGuid(), row.TenantId, row.MessageId),
                    cancellationToken);
                published++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await source.ReleaseAsync(row, CancellationToken.None);
                throw;
            }
            catch (Exception exception)
            {
                await source.ReleaseAsync(row, cancellationToken);
                logger.LogError(exception, "Failed to publish SMS message {MessageId} for tenant {TenantId}.", row.MessageId, row.TenantId);
            }
        }

        return published;
    }
}
