using Sms.Api.Features.Messages;
using Sms.Api.Features.Messages;

namespace Sms.Api.Features.Messages;

/// <summary>In-memory provider for local and automated flow tests. It never calls an external service.</summary>
public sealed class MockSmsProvider : ISmsProvider
{
    public string Name => "Mock";

    public Task<ProviderSendResult> SendAsync(string from, string to, string body, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var statuses = new[] { SmsStatus.Pending, SmsStatus.Sent, SmsStatus.Delivered, SmsStatus.Failed };
        var status = statuses[Random.Shared.Next(statuses.Length)];
        return Task.FromResult(new ProviderSendResult($"mock-{Guid.NewGuid():N}", status));
    }
}
