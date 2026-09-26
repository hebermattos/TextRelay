using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Sms.Application.Messages;
using Sms.Infrastructure.Caching;

namespace Sms.Infrastructure.Persistence;

public sealed class TenantAiSettingsRepository(
    SqlConnectionFactory connectionFactory,
    ResilientDistributedCache cache) : ITenantAiSettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const string DefaultImprovePrompt = "Improve this SMS. Keep the meaning, make it concise and professional, preserve every {{variableName}} exactly, do not add facts. Return only the improved SMS.";
    public const string DefaultValidatePrompt = "Review this SMS or message template and give concise, actionable suggestions to improve clarity, spelling, tone, length, and ambiguous wording. Check broken {{variableName}} placeholders and preserve variables exactly. Do not rewrite the message and do not judge legal compliance. Return JSON only: {\"isValid\":true,\"issues\":[\"...\"]}. Set isValid to false when you have improvement suggestions.";

    public async Task<TenantAiSettings> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var key = CacheKey(tenantId);
        var cached = await cache.GetStringAsync(key, "AiSettings", tenantId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            var value = JsonSerializer.Deserialize<TenantAiSettings>(cached, JsonOptions);
            if (value is not null) return value;
        }

        using var connection = connectionFactory.CreateConnection();
        var settings = await connection.QuerySingleOrDefaultAsync<TenantAiSettings>(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/TenantAiSettingsRepository.GetAsync.01.sql"),
            new { TenantId = tenantId }, cancellationToken: cancellationToken))
            ?? new(DefaultImprovePrompt, DefaultValidatePrompt);

        await cache.SetStringAsync(key, JsonSerializer.Serialize(settings, JsonOptions), "AiSettings", tenantId, cancellationToken);
        return settings;
    }

    public async Task SaveAsync(Guid tenantId, TenantAiSettings settings, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/TenantAiSettingsRepository.SaveAsync.01.sql"),
            new { TenantId = tenantId, settings.ImprovePrompt, settings.ValidatePrompt, UpdatedAt = DateTimeOffset.UtcNow },
            cancellationToken: cancellationToken));

        await cache.RemoveAsync(CacheKey(tenantId), "AiSettings", tenantId, CancellationToken.None);
    }

    private static string CacheKey(Guid tenantId) => $"tenant-config:ai:{tenantId:N}";
}
