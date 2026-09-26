using Sms.Domain.Messages;
using Sms.Application.OptOut;

namespace Sms.Application.Messages;

public sealed class ReceiveSmsWebhookService(ISmsMessageRepository messages, OptOutService optOut)
{
    public async Task<SmsWebhookResult> ReceiveAsync(
        ISmsWebhookParser parser,
        string authorization,
        Stream body,
        SmsDirection direction,
        CancellationToken cancellationToken = default)
    {
        // Authenticate and validate the entire batch before making any changes.
        var batch = await parser.ParseAsync(authorization, body, direction, cancellationToken);
        if (batch.Result != SmsWebhookResult.Accepted)
            return batch.Result;

        foreach (var webhookEvent in batch.Events)
        {
            if (webhookEvent.Direction == SmsDirection.Inbound)
            {
                await ProcessInboundAsync(webhookEvent, cancellationToken);
                continue;
            }

            await messages.UpdateStatusByProviderMessageIdAsync(
                webhookEvent.TenantId,
                webhookEvent.Provider,
                webhookEvent.ProviderMessageId,
                webhookEvent.Status,
                webhookEvent.Time,
                cancellationToken);
        }

        return SmsWebhookResult.Accepted;
    }

    private async Task ProcessInboundAsync(ValidatedSmsWebhook webhookEvent, CancellationToken cancellationToken)
    {
        await optOut.ProcessInboundAsync(
            webhookEvent.TenantId,
            webhookEvent.From,
            webhookEvent.Body,
            webhookEvent.Time,
            cancellationToken);

        var message = new SmsMessage
        {
            Id = Guid.NewGuid(),
            TenantId = webhookEvent.TenantId,
            Provider = webhookEvent.Provider,
            ProviderMessageId = webhookEvent.ProviderMessageId,
            Direction = webhookEvent.Direction,
            QueueStatus = SmsQueueStatus.Queued,
            Status = webhookEvent.Status,
            From = webhookEvent.From,
            To = webhookEvent.To,
            Body = webhookEvent.Body,
            CreatedAt = webhookEvent.Time
        };

        await messages.InsertInboundIfNotExistsAsync(message, cancellationToken);
    }
}
