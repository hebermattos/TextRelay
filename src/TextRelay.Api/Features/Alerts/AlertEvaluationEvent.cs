using Sms.Domain.Messages;

namespace Sms.Infrastructure.Messaging;

public sealed record AlertEvaluationEvent(
    Guid EventId,
    Guid TenantId,
    string Provider,
    SmsStatus Status,
    DateTimeOffset OccurredAtUtc);
