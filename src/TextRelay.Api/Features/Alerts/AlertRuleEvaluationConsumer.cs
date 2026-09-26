using MassTransit;
using Sms.Api.Features.Alerts;

namespace Sms.Api.Features.Alerts;

public sealed class AlertRuleEvaluationConsumer(IAlertRepository alerts) : IConsumer<EvaluateAlertRuleEvent>
{
    public Task Consume(ConsumeContext<EvaluateAlertRuleEvent> context)
    {
        var message = context.Message;
        return alerts.EvaluateRuleAsync(
            message.EventId,
            message.AlertRuleId,
            message.TenantId,
            message.OccurredAtUtc,
            context.CancellationToken);
    }
}
