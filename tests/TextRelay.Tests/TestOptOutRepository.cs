using Sms.Application.OptOut;

namespace Sms.Infrastructure.Tests;

internal sealed class TestOptOutRepository : IOptOutRepository
{
    private readonly Dictionary<(Guid TenantId, string Phone), BlockedNumber> _items = [];
    public Task<IReadOnlyList<BlockedNumber>> ListAsync(Guid tenantId, int skip, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BlockedNumber>>(_items.Where(x => x.Key.TenantId == tenantId).Select(x => x.Value).Skip(skip).Take(take).ToArray());
    public Task<bool> IsBlockedAsync(Guid tenantId, string phoneNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult(_items.ContainsKey((tenantId, phoneNumber)));
    public Task AddOrUpdateAsync(Guid tenantId, string phoneNumber, string source, string? reason, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        var exists = _items.TryGetValue((tenantId, phoneNumber), out var value);
        var id = exists ? value!.Id : Guid.NewGuid();
        _items[(tenantId, phoneNumber)] = new(id, phoneNumber, source, reason,
            exists ? value!.CreatedAt : occurredAt, exists ? occurredAt : null);
        return Task.CompletedTask;
    }
    public Task<bool> RemoveAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        var item = _items.SingleOrDefault(x => x.Key.TenantId == tenantId && x.Value.Id == id);
        return Task.FromResult(!item.Equals(default(KeyValuePair<(Guid, string), BlockedNumber>)) && _items.Remove(item.Key));
    }
    public Task RemoveByPhoneAsync(Guid tenantId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        _items.Remove((tenantId, phoneNumber));
        return Task.CompletedTask;
    }
}
