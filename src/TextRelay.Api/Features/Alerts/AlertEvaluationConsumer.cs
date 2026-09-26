using Dapper;
using MassTransit;
using Sms.Application.Alerts;
using Sms.Infrastructure.Persistence;

namespace Sms.Infrastructure.Messaging;

public sealed class AlertEvaluationConsumer(
    ReportingSqlConnectionFactory reportingConnectionFactory,
    IAlertRepository alerts,
    IPublishEndpoint publishEndpoint) : IConsumer<AlertEvaluationEvent>
{
    public async Task Consume(ConsumeContext<AlertEvaluationEvent> context)
    {
        var message = context.Message;
        using var connection = reportingConnectionFactory.CreateConnection();

        await connection.ExecuteAsync(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Messaging/AlertEvaluationConsumer.Consume.01.sql"),
            new { message.EventId, message.TenantId, message.Provider, message.Status, message.OccurredAtUtc },
            cancellationToken: context.CancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Messaging/AlertEvaluationConsumer.Consume.02.sql"),
            cancellationToken: context.CancellationToken));

        var rules = await alerts.ListRulesAsync(message.TenantId, context.CancellationToken);
        foreach (var rule in rules.Where(rule =>
                     rule.IsActive &&
                     rule.Status == message.Status &&
                     (rule.Provider is null || rule.Provider == message.Provider)))
        {
            await publishEndpoint.Publish(
                new EvaluateAlertRuleEvent(message.EventId, rule.Id, message.TenantId, message.OccurredAtUtc),
                context.CancellationToken);
        }
    }
}
