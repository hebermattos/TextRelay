using MassTransit;
using System.Diagnostics;
using Sms.Application.Messages;
using Sms.Infrastructure.Observability;

namespace Sms.Infrastructure.Messaging;

public sealed class SmsSendEventPublisher(IPublishEndpoint publishEndpoint) : ISmsSendEventPublisher
{
    public async Task PublishAsync(Guid tenantId, Guid messageId, CancellationToken cancellationToken = default)
    {
        using var activity = TextRelayTelemetry.ActivitySource.StartActivity("sms.queue.publish", ActivityKind.Producer);
        activity?.SetTag("tenant.id", tenantId);
        activity?.SetTag("message.id", messageId);
        try
        {
            await publishEndpoint.Publish(new SmsSendEvent(Guid.NewGuid(), tenantId, messageId), cancellationToken);
            TextRelayTelemetry.SmsQueued.Add(1);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.RecordException(exception);
            TextRelayTelemetry.QueuePublishFailed.Add(1);
            throw;
        }
    }
}
