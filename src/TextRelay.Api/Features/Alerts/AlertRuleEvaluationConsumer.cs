using MassTransit;
using Sms.Application.Alerts;

namespace Sms.Infrastructure.Messaging;

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
