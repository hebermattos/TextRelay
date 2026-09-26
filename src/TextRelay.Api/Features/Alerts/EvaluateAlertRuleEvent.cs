namespace Sms.Infrastructure.Messaging;

public sealed record EvaluateAlertRuleEvent(
    Guid EventId,
    Guid AlertRuleId,
    Guid TenantId,
    DateTimeOffset OccurredAtUtc);
