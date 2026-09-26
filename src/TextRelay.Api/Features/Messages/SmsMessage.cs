namespace Sms.Domain.Messages;

public enum SmsDirection { Outbound = 1, Inbound = 2 }
public enum SmsQueueStatus { NotQueued = 1, Queued = 2, Scheduled = 3, Processing = 4 }
public enum SmsStatus { Pending = 1, Sent = 2, Delivered = 3, Failed = 4, Received = 5 }

public sealed class SmsMessage
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid? UserId { get; init; }
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string? ProviderMessageId { get; set; }
    public SmsDirection Direction { get; init; }
    public SmsQueueStatus QueueStatus { get; set; }
    public SmsStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ScheduledAtUtc { get; init; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
