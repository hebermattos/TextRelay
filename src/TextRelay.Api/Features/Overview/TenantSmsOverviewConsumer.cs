using MassTransit;

namespace Sms.Infrastructure.Messaging;

public sealed class TenantSmsOverviewConsumer(ITenantSmsOverviewProjection projection) : IConsumer<TenantSmsOverviewEvent>
{
    public async Task Consume(ConsumeContext<TenantSmsOverviewEvent> context)
        => await ApplyAsync(context.Message, context.CancellationToken);

    public async Task ApplyAsync(TenantSmsOverviewEvent item, CancellationToken cancellationToken = default)
        => await projection.ApplyAsync(item, cancellationToken);
}
