using Dapper;
using Sms.Application.Alerts;

namespace Sms.Infrastructure.Persistence;

public sealed class AlertRepository(
    SqlConnectionFactory connectionFactory,
    ReportingSqlConnectionFactory reportingConnectionFactory) : IAlertRepository
{
    public async Task<IReadOnlyList<AlertRule>> ListRulesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/AlertRepository.ListRulesAsync.01.sql");
        using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<AlertRule>(new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<AlertRule?> GetRuleAsync(Guid tenantId, Guid ruleId, CancellationToken cancellationToken = default)
    {
        var sql = Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/AlertRepository.GetRuleAsync.10.sql");
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<AlertRule>(new CommandDefinition(sql, new { TenantId = tenantId, RuleId = ruleId }, cancellationToken: cancellationToken));
    }

    public async Task CreateRuleAsync(AlertRule rule, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/AlertRepository.CreateRuleAsync.02.sql"), rule, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateRuleAsync(AlertRule rule, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/AlertRepository.UpdateRuleAsync.03.sql"), rule, cancellationToken: cancellationToken)) == 1;
    }

    public async Task<bool> DeleteRuleAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/AlertRepository.DeleteRuleAsync.06.sql"), new { TenantId = tenantId, Id = id }, cancellationToken: cancellationToken)) == 1;
    }

    public async Task<IReadOnlyList<AlertNotification>> ListAlertsAsync(Guid tenantId, bool unreadOnly, int skip, int take, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return (await connection.QueryAsync<AlertNotification>(new CommandDefinition(Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/AlertRepository.ListAlertsAsync.04.sql"), new { TenantId = tenantId, UnreadOnly = unreadOnly, Skip = skip, Take = take }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<bool> MarkReadAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/AlertRepository.MarkReadAsync.07.sql"), new { TenantId = tenantId, Id = id }, cancellationToken: cancellationToken)) == 1;
    }

    public async Task MarkAllReadAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/AlertRepository.MarkAllReadAsync.08.sql"), new { TenantId = tenantId }, cancellationToken: cancellationToken));
    }

    public async Task EvaluateRuleAsync(Guid eventId, Guid ruleId, Guid tenantId, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default)
    {
        var rule = await GetRuleAsync(tenantId, ruleId, cancellationToken);
        if (rule is null || !rule.IsActive) return;

        using var reporting = reportingConnectionFactory.CreateConnection();
        var matchCount = await reporting.ExecuteScalarAsync<int>(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/AlertRepository.EvaluateRuleAsync.11.sql"),
            new { TenantId = tenantId, rule.Status, rule.Provider, rule.WindowMinutes, OccurredAtUtc = occurredAtUtc },
            cancellationToken: cancellationToken));
        if (matchCount == 0) return;

        using var application = connectionFactory.CreateConnection();
        await application.ExecuteAsync(new CommandDefinition(
            Sms.Infrastructure.Sql.SqlQuery.Load("Persistence/AlertRepository.FireAsync.09.sql"),
            new { TenantId = tenantId, EventId = eventId, RuleId = rule.Id, RuleName = rule.Name, rule.Provider, rule.Status, MatchCount = matchCount, rule.WindowMinutes, OccurredAtUtc = occurredAtUtc },
            cancellationToken: cancellationToken));
    }
}
