namespace Sms.Api.Features.Alerts;

public sealed record EvaluateAlertRuleEvent(
    Guid EventId,
    Guid AlertRuleId,
    Guid TenantId,
    DateTimeOffset OccurredAtUtc);
