namespace Sms.Application.Templates;

public sealed record MessageTemplate(Guid Id, string Name, string Body, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public interface IMessageTemplateRepository
{
    Task<IReadOnlyList<MessageTemplate>> ListAsync(Guid tenantId, int skip, int take, CancellationToken cancellationToken = default);
    Task<MessageTemplate?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<MessageTemplate> CreateAsync(Guid tenantId, string name, string body, CancellationToken cancellationToken = default);
    Task<MessageTemplate?> UpdateAsync(Guid tenantId, Guid id, string name, string body, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
}
