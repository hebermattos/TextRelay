namespace Sms.Infrastructure.Messaging;

public sealed record SmsSendEvent(Guid EventId, Guid TenantId, Guid MessageId);
