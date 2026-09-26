namespace Sms.Application.Messages;

public sealed record TenantAiSettings(string ImprovePrompt, string ValidatePrompt);

public interface ITenantAiSettingsRepository
{
    Task<TenantAiSettings> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task SaveAsync(Guid tenantId, TenantAiSettings settings, CancellationToken cancellationToken = default);
}
