namespace Sms.Domain.Messages;

public sealed class SmsStatusHistory
{
    public Guid Id { get; init; }
    public Guid MessageId { get; init; }
    public SmsStatus Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
