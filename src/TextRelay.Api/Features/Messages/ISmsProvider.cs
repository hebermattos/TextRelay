using Sms.Domain.Messages;

namespace Sms.Application.Messages;

public sealed record SendSmsRequest(string To, string Body, string? From = null, string? Provider = null,
    DateTime? ScheduledAt = null, Guid? UserId = null);
public sealed record SendSmsResult(Guid Id, string Provider, string? ProviderMessageId, string Status,
    DateTimeOffset? ScheduledAt = null);

public interface ISmsProvider
{
    string Name { get; }
    Task<ProviderSendResult> SendAsync(string from, string to, string body, CancellationToken cancellationToken = default);
}

public sealed record ProviderSendResult(string ProviderMessageId, SmsStatus Status);
