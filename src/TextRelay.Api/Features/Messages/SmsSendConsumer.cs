using MassTransit;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Sms.Application.Common;
using Sms.Application.Messages;
using Sms.Application.OptOut;
using Sms.Domain.Messages;
using Sms.Infrastructure.Observability;

namespace Sms.Infrastructure.Messaging;

public sealed class SmsSendConsumer(
    IWorkerTenantContext tenantContext,
    ISmsMessageRepository repository,
    ISmsProviderResolver providerResolver,
    OptOutService optOut,
    ILogger<SmsSendConsumer> logger) : IConsumer<SmsSendEvent>
{
    public async Task Consume(ConsumeContext<SmsSendEvent> context)
    {
        var sendEvent = context.Message;
        using var activity = TextRelayTelemetry.ActivitySource.StartActivity("sms.queue.consume", ActivityKind.Consumer);
        activity?.SetTag("tenant.id", sendEvent.TenantId);
        activity?.SetTag("message.id", sendEvent.MessageId);
        tenantContext.SetTenant(sendEvent.TenantId);

        var message = await repository.GetByIdAsync(
            sendEvent.TenantId,
            sendEvent.MessageId,
            context.CancellationToken)
            ?? throw new InvalidOperationException("Queued SMS message was not found.");

        if (IsScheduledMessageEvent(sendEvent))
        {
            var queued = await repository.TryQueueScheduledAsync(
                sendEvent.TenantId,
                sendEvent.MessageId,
                DateTimeOffset.UtcNow,
                context.CancellationToken);

            if (!queued)
                return;

            message.QueueStatus = SmsQueueStatus.Queued;
        }

        var redeliveryCount = context.GetRedeliveryCount();
        var isProviderRetry = redeliveryCount > 0 && message.QueueStatus == SmsQueueStatus.Processing;

        if (!isProviderRetry)
        {
            if (message.QueueStatus != SmsQueueStatus.Queued)
                return;

            bool claimed;
            using (var claimActivity = TextRelayTelemetry.ActivitySource.StartActivity("sms.queue.claim", ActivityKind.Internal))
            {
                claimActivity?.SetTag("tenant.id", sendEvent.TenantId);
                claimActivity?.SetTag("message.id", sendEvent.MessageId);
                claimed = await repository.TryClaimQueuedAsync(
                    sendEvent.TenantId,
                    sendEvent.MessageId,
                    DateTimeOffset.UtcNow,
                    context.CancellationToken);
                claimActivity?.SetTag("sms.queue.claimed", claimed);
            }

            if (!claimed)
            {
                TextRelayTelemetry.SmsClaimRejected.Add(1);
                return;
            }

            message.QueueStatus = SmsQueueStatus.Processing;
        }

        activity?.SetTag("messaging.redelivery.count", redeliveryCount);
        var processingStarted = Stopwatch.GetTimestamp();
        try
        {
            await SendAsync(sendEvent, message, context.CancellationToken);
        }
        finally
        {
            TextRelayTelemetry.ProcessingDuration.Record(Stopwatch.GetElapsedTime(processingStarted).TotalMilliseconds);
        }
    }

    private async Task SendAsync(SmsSendEvent sendEvent, SmsMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await optOut.EnsureCanSendAsync(sendEvent.TenantId, message.To, cancellationToken);

            var provider = providerResolver.Resolve(message.Provider);
            using var providerActivity = TextRelayTelemetry.ActivitySource.StartActivity("sms.provider.send", ActivityKind.Client);
            providerActivity?.SetTag("tenant.id", sendEvent.TenantId);
            providerActivity?.SetTag("message.id", sendEvent.MessageId);
            providerActivity?.SetTag("sms.provider", provider.Name);
            var started = Stopwatch.GetTimestamp();
            ProviderSendResult sendResult;
            try
            {
                sendResult = await provider.SendAsync(message.From, message.To, message.Body, cancellationToken);
            }
            catch (Exception exception)
            {
                providerActivity?.SetStatus(ActivityStatusCode.Error, exception.Message);
                providerActivity?.RecordException(exception);
                throw;
            }
            finally
            {
                TextRelayTelemetry.ProviderDuration.Record(
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    new KeyValuePair<string, object?>("sms.provider", provider.Name));
            }
            providerActivity?.SetTag("sms.status", sendResult.Status.ToString());
            if (sendResult.Status == SmsStatus.Failed)
                TextRelayTelemetry.SmsFailed.Add(1, new KeyValuePair<string, object?>("sms.provider", provider.Name));
            else
                TextRelayTelemetry.SmsSent.Add(1, new KeyValuePair<string, object?>("sms.provider", provider.Name));
            await repository.UpdateStatusAsync(
                sendEvent.TenantId,
                sendEvent.MessageId,
                sendResult.Status,
                sendResult.ProviderMessageId,
                DateTimeOffset.UtcNow,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TransientSmsProviderException exception)
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error, exception.Message);
            Activity.Current?.RecordException(exception);
            // Keep the persisted state as Processing. Delayed redelivery creates a
            // new delivery for the same message after the configured backoff; that
            // delivery may continue without claiming again, while unrelated duplicates
            // with no redelivery metadata remain rejected.
            throw;
        }
        catch (Exception exception)
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error, exception.Message);
            Activity.Current?.RecordException(exception);
            TextRelayTelemetry.SmsFailed.Add(1, new KeyValuePair<string, object?>("sms.provider", message.Provider));
            logger.LogError(exception, "Failed to process queued SMS message {MessageId} for tenant {TenantId} with provider {Provider}.", sendEvent.MessageId, sendEvent.TenantId, message.Provider);

            await repository.UpdateStatusAsync(
                sendEvent.TenantId,
                sendEvent.MessageId,
                SmsStatus.Failed,
                null,
                DateTimeOffset.UtcNow,
                cancellationToken);
        }
    }

    private static bool IsScheduledMessageEvent(SmsSendEvent sendEvent) =>
        sendEvent.EventId == sendEvent.MessageId;
}
