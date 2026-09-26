using Sms.Domain.Messages;

namespace Sms.Application.Messages;

public enum SmsWebhookResult { Accepted, Invalid, Unauthorized }

public sealed record ValidatedSmsWebhook(
    Guid TenantId, string Provider, string ProviderMessageId, SmsDirection Direction,
    SmsStatus Status, string From, string To, string Body, DateTimeOffset Time);

public sealed record SmsWebhookBatch(SmsWebhookResult Result, IReadOnlyList<ValidatedSmsWebhook> Events);

public interface ISmsWebhookParser
{
    Task<SmsWebhookBatch> ParseAsync(string authorization, Stream body, SmsDirection direction,
        CancellationToken cancellationToken = default);
}
