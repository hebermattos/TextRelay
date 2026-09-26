using Dapper;
using MassTransit;
using Sms.Api.Features.Alerts;
using Sms.Api.Features.Administration;
using Sms.Api.Features.Alerts;
using Sms.Api.Features.Auth;
using Sms.Api.Features.Logs;
using Sms.Api.Features.Messages;
using Sms.Api.Features.OptOuts;
using Sms.Api.Features.Overview;
using Sms.Api.Features.Providers;
using Sms.Api.Features.Reports;
using Sms.Api.Features.Templates;
using Sms.Api.Features.Tenants;
using Sms.Api.Shared.Persistence;
using Sms.Api.Shared.Tenancy;

namespace Sms.Api.Features.Alerts;

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
            Sms.Api.Shared.Persistence.SqlQuery.Load("Messaging/AlertEvaluationConsumer.Consume.01.sql"),
            new { message.EventId, message.TenantId, message.Provider, message.Status, message.OccurredAtUtc },
            cancellationToken: context.CancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            Sms.Api.Shared.Persistence.SqlQuery.Load("Messaging/AlertEvaluationConsumer.Consume.02.sql"),
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
