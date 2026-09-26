using Sms.Api.Features.Messages;

namespace Sms.Api.Features.Alerts;

public sealed record AlertEvaluationEvent(
    Guid EventId,
    Guid TenantId,
    string Provider,
    SmsStatus Status,
    DateTimeOffset OccurredAtUtc);
