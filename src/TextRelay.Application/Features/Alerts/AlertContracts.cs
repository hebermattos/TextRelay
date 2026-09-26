using Sms.Domain.Messages;

namespace Sms.Application.Alerts;

public sealed record AlertRule(
    Guid Id, Guid TenantId, string Name, string? Provider, SmsStatus Status,
    int WindowMinutes, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record AlertNotification(
    Guid Id, Guid RuleId, string RuleName, string? Provider, SmsStatus Status,
    int MatchCount, int WindowMinutes, DateTimeOffset CreatedAt, bool IsRead,
    DateTimeOffset? ReadAt);

public sealed record SaveAlertRule(
    string Name, string? Provider, SmsStatus Status, int WindowMinutes, bool IsActive);

public interface IAlertRepository
{
    Task<IReadOnlyList<AlertRule>> ListRulesAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task CreateRuleAsync(AlertRule rule, CancellationToken cancellationToken = default);
    Task<bool> UpdateRuleAsync(AlertRule rule, CancellationToken cancellationToken = default);
    Task<bool> DeleteRuleAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AlertNotification>> ListAlertsAsync(Guid tenantId, bool unreadOnly, int skip, int take, CancellationToken cancellationToken = default);
    Task<bool> MarkReadAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
    Task MarkAllReadAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<AlertRule?> GetRuleAsync(Guid tenantId, Guid ruleId, CancellationToken cancellationToken = default);
    Task EvaluateRuleAsync(Guid eventId, Guid ruleId, Guid tenantId, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default);
}
