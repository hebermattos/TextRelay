namespace Sms.Application.Messages;

public interface IMessageAssistant
{
    Task<MessageAssistantResult> ImproveAsync(Guid tenantId, string message, CancellationToken cancellationToken = default);
    Task<MessageAssistantResult> ValidateAsync(Guid tenantId, string message, CancellationToken cancellationToken = default);
}

public sealed record MessageAssistantResult(string Message, IReadOnlyList<string> Issues, bool IsValid);
