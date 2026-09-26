namespace Sms.Api.Features.Messages;

public interface ISmsSendEventPublisher
{
    Task PublishAsync(Guid tenantId, Guid messageId, CancellationToken cancellationToken = default);
}
