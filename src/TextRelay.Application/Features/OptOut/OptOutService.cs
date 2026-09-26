namespace Sms.Application.OptOut;

public sealed class OptOutService(IOptOutRepository repository)
{
    private const int MaxImportSize = 1_000;
    private const int MaxReasonLength = 200;

    private static readonly HashSet<string> StopKeywords = new(StringComparer.OrdinalIgnoreCase)
        { "STOP", "UNSUBSCRIBE", "CANCEL" };
    private static readonly HashSet<string> StartKeywords = new(StringComparer.OrdinalIgnoreCase)
        { "START" };

    public Task<IReadOnlyList<BlockedNumber>> ListAsync(Guid tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        if (skip < 0)
            throw new ArgumentException("skip must be zero or greater.");

        return repository.ListAsync(tenantId, skip, Math.Clamp(take, 1, 200), cancellationToken);
    }

    public Task AddAsync(Guid tenantId, string phoneNumber, string? reason, CancellationToken cancellationToken = default) =>
        repository.AddOrUpdateAsync(
            tenantId,
            PhoneNumberNormalizer.Normalize(phoneNumber),
            "Manual",
            CleanReason(reason),
            DateTimeOffset.UtcNow,
            cancellationToken);

    public async Task ImportAsync(Guid tenantId, IEnumerable<AddBlockedNumber> rows, CancellationToken cancellationToken = default)
    {
        var importRows = rows.Take(MaxImportSize + 1).ToArray();
        if (importRows.Length > MaxImportSize)
            throw new ArgumentException($"A maximum of {MaxImportSize:N0} numbers can be imported at once.");

        foreach (var row in importRows)
        {
            await repository.AddOrUpdateAsync(
                tenantId,
                PhoneNumberNormalizer.Normalize(row.PhoneNumber),
                "Import",
                CleanReason(row.Reason),
                DateTimeOffset.UtcNow,
                cancellationToken);
        }
    }

    public Task<bool> RemoveAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default) =>
        repository.RemoveAsync(tenantId, id, cancellationToken);

    public async Task EnsureCanSendAsync(Guid tenantId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var normalizedPhoneNumber = PhoneNumberNormalizer.Normalize(phoneNumber);
        if (await repository.IsBlockedAsync(tenantId, normalizedPhoneNumber, cancellationToken))
            throw new BlockedRecipientException();
    }

    public async Task ProcessInboundAsync(Guid tenantId, string phoneNumber, string body, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        var keyword = body.Trim();
        var normalizedPhoneNumber = PhoneNumberNormalizer.Normalize(phoneNumber);

        if (StopKeywords.Contains(keyword))
        {
            await repository.AddOrUpdateAsync(
                tenantId,
                normalizedPhoneNumber,
                "InboundKeyword",
                keyword.ToUpperInvariant(),
                occurredAt,
                cancellationToken);
        }
        else if (StartKeywords.Contains(keyword))
        {
            await repository.RemoveByPhoneAsync(tenantId, normalizedPhoneNumber, cancellationToken);
        }
    }

    private static string? CleanReason(string? reason)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason?.Length > MaxReasonLength)
            throw new ArgumentException($"Reason must be {MaxReasonLength} characters or fewer.");

        return reason;
    }
}
