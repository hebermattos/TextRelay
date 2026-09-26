namespace Sms.Api.Features.Messages;

public sealed record SmsSendEvent(Guid EventId, Guid TenantId, Guid MessageId);
