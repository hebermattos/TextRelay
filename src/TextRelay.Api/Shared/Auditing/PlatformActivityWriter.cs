using Dapper;
using Npgsql;

namespace Sms.Api.Middleware;

public interface IPlatformActivityWriter
{
    Task WriteAsync(PlatformActivity activity, CancellationToken cancellationToken = default);
}

public sealed record PlatformActivity(
    string? UserId,
    string ActivityType,
    string Action,
    string? ResourceType,
    string? ResourceId,
    string Description,
    string Outcome);

public sealed class PostgresPlatformActivityWriter(
    string connectionString,
    TimeProvider timeProvider,
    ILogger<PostgresPlatformActivityWriter> logger) : IPlatformActivityWriter
{
    private const string Sql = """
        INSERT INTO PlatformActivityLogs
            ("Timestamp", UserId, ActivityType, Action, ResourceType, ResourceId, Description, Outcome)
        VALUES
            (@Timestamp, @UserId, @ActivityType, @Action, @ResourceType, @ResourceId, @Description, @Outcome);
        """;

    public async Task WriteAsync(PlatformActivity activity, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.ExecuteAsync(new CommandDefinition(Sql, new
            {
                Timestamp = timeProvider.GetUtcNow(),
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
            logger.LogError(exception, "Could not persist platform activity {Action} for user {UserId}, resource {ResourceType} {ResourceId}.", activity.Action, activity.UserId, activity.ResourceType, activity.ResourceId);
        }
    }
}
