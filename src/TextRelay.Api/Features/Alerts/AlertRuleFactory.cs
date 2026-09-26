using Sms.Domain.Messages;

namespace Sms.Application.Alerts;

public sealed class AlertRuleFactory(TimeProvider clock)
{
    public AlertRule Create(Guid tenantId, SaveAlertRule request) => Build(Guid.NewGuid(), tenantId, request);
    public AlertRule Update(Guid id, Guid tenantId, SaveAlertRule request) => Build(id, tenantId, request);

    private AlertRule Build(Guid id, Guid tenantId, SaveAlertRule request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            throw new ArgumentException("Rule name is required and must not exceed 120 characters.");
        if (!Enum.IsDefined(request.Status))
            throw new ArgumentException("A valid SMS status is required.");
        if (request.WindowMinutes is < 1 or > 1440)
            throw new ArgumentException("Time window must be between 1 minute and 24 hours.");

        var provider = string.IsNullOrWhiteSpace(request.Provider) ? null : request.Provider.Trim();
        if (provider?.Length > 50)
            throw new ArgumentException("Provider must not exceed 50 characters.");

        return new AlertRule(id, tenantId, name, provider, request.Status, request.WindowMinutes,
            request.IsActive, clock.GetUtcNow(), null);
    }
}
