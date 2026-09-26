using Dapper;
using Npgsql;

namespace Sms.Api.Middleware;

public interface IUserActivityWriter
{
    Task WriteAsync(UserActivity activity, CancellationToken cancellationToken = default);
}

public sealed record UserActivity(
    Guid TenantId,
    string? UserId,
    string ActivityType,
    string Action,
    string? ResourceType,
    string? ResourceId,
    string Description,
    string Outcome);

public sealed class PostgresUserActivityWriter(string connectionString, TimeProvider timeProvider, ILogger<PostgresUserActivityWriter> logger) : IUserActivityWriter
{
    private const string Sql = """
        INSERT INTO UserActivityLogs
            ("Timestamp", TenantId, UserId, ActivityType, Action, ResourceType, ResourceId, Description, Outcome)
        VALUES
            (@Timestamp, @TenantId, @UserId, @ActivityType, @Action, @ResourceType, @ResourceId, @Description, @Outcome);
        """;

    public async Task WriteAsync(UserActivity activity, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.ExecuteAsync(new CommandDefinition(Sql, new
            {
                Timestamp = timeProvider.GetUtcNow(),
                activity.TenantId,
                activity.UserId,
                activity.ActivityType,
                activity.Action,
                activity.ResourceType,
                activity.ResourceId,
                activity.Description,
                activity.Outcome
            }, cancellationToken: cancellationToken));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not persist user activity {Action} for tenant {TenantId}, user {UserId}, resource {ResourceType} {ResourceId}.", activity.Action, activity.TenantId, activity.UserId, activity.ResourceType, activity.ResourceId);
        }
    }
}
