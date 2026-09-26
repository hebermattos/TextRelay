namespace Sms.Application.Alerts;

public sealed class AlertService(IAlertRepository repository, AlertRuleFactory ruleFactory)
{
    public Task<IReadOnlyList<AlertRule>> ListRulesAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        repository.ListRulesAsync(tenantId, cancellationToken);

    public async Task<Guid> CreateRuleAsync(Guid tenantId, SaveAlertRule request, CancellationToken cancellationToken = default)
    {
        var rule = ruleFactory.Create(tenantId, request);
        await repository.CreateRuleAsync(rule, cancellationToken);
        return rule.Id;
    }

    public async Task UpdateRuleAsync(Guid tenantId, Guid id, SaveAlertRule request, CancellationToken cancellationToken = default)
    {
        var rule = ruleFactory.Update(id, tenantId, request);
        if (!await repository.UpdateRuleAsync(rule, cancellationToken))
            throw new KeyNotFoundException("Alert rule was not found.");
    }

    public async Task DeleteRuleAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        if (!await repository.DeleteRuleAsync(tenantId, id, cancellationToken))
            throw new KeyNotFoundException("Alert rule was not found.");
    }

    public Task<IReadOnlyList<AlertNotification>> ListAlertsAsync(
        Guid tenantId, bool unreadOnly, int skip, int take, CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take is < 1 or > 200) throw new ArgumentException("Invalid pagination.");
        return repository.ListAlertsAsync(tenantId, unreadOnly, skip, take, cancellationToken);
    }

    public async Task MarkReadAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        if (!await repository.MarkReadAsync(tenantId, id, cancellationToken))
            throw new KeyNotFoundException("Alert was not found.");
    }

    public Task MarkAllReadAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        repository.MarkAllReadAsync(tenantId, cancellationToken);
}
