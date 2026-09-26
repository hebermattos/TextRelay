namespace Sms.Application.OptOut;

public sealed record BlockedNumber(
    Guid Id, string PhoneNumber, string Source, string? Reason,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record AddBlockedNumber(string PhoneNumber, string? Reason = null);

public interface IOptOutRepository
{
    Task<IReadOnlyList<BlockedNumber>> ListAsync(Guid tenantId, int skip, int take, CancellationToken cancellationToken = default);
    Task<bool> IsBlockedAsync(Guid tenantId, string phoneNumber, CancellationToken cancellationToken = default);
    Task AddOrUpdateAsync(Guid tenantId, string phoneNumber, string source, string? reason, DateTimeOffset occurredAt, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
    Task RemoveByPhoneAsync(Guid tenantId, string phoneNumber, CancellationToken cancellationToken = default);
}

public sealed class BlockedRecipientException : InvalidOperationException
{
    public BlockedRecipientException() : base("The destination phone number has opted out of SMS messages.") { }
}
