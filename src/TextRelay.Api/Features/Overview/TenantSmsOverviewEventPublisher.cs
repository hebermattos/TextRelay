using MassTransit;

namespace Sms.Infrastructure.Messaging;

public sealed class TenantSmsOverviewEventPublisher(IBus bus) : ITenantSmsOverviewEventPublisher
{
    public Task PublishAsync(TenantSmsOverviewEvent item, CancellationToken cancellationToken = default) =>
        bus.Publish(item, cancellationToken);
}
