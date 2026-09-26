using Sms.Application.Common;
using Sms.Application.OptOut;
using Sms.Domain.Messages;

namespace Sms.Application.Messages;

public sealed class SendSmsService(
    ITenantContext tenantContext,
    ISmsMessageRepository repository,
    ISmsProviderResolver providerResolver,
    ISmsSendEventPublisher eventPublisher,
    OptOutService optOut,
    SendSmsValidator validator,
    TimeProvider clock)
{
    public async Task<SendSmsResult> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var scheduledAtUtc = await validator.ValidateAsync(request, now, cancellationToken);

        await optOut.EnsureCanSendAsync(tenantContext.TenantId, request.To, cancellationToken);

        var provider = providerResolver.Resolve(request.Provider);
        var queueStatus = scheduledAtUtc.HasValue ? SmsQueueStatus.Scheduled : SmsQueueStatus.NotQueued;
        var message = new SmsMessage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.TenantId,
            UserId = request.UserId,
            From = request.From ?? string.Empty,
            To = request.To.Trim(),
            Body = request.Body,
            Provider = provider.Name,
            Direction = SmsDirection.Outbound,
            QueueStatus = queueStatus,
            Status = SmsStatus.Pending,
            CreatedAt = now,
            ScheduledAtUtc = scheduledAtUtc
        };

        await repository.InsertAsync(message, cancellationToken);
        if (!scheduledAtUtc.HasValue)
        {
            await repository.UpdateQueueStatusAsync(
                message.TenantId,
                message.Id,
                SmsQueueStatus.Queued,
                clock.GetUtcNow(),
                cancellationToken);
            queueStatus = SmsQueueStatus.Queued;

            // Persist Queued before publishing. If publishing fails, the recovery worker
            // can safely find and publish the queued message later.
            await eventPublisher.PublishAsync(message.TenantId, message.Id, cancellationToken);
        }

        return new SendSmsResult(message.Id, provider.Name, null, queueStatus.ToString(), scheduledAtUtc);
    }
}
