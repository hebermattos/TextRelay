using StackExchange.Redis;

namespace Sms.Api.RateLimiting;

public interface IRateLimitCounter
{
    Task<long> IncrementAsync(string key, TimeSpan window, CancellationToken cancellationToken = default);
}

public sealed class RedisRateLimitCounter(IConnectionMultiplexer redis) : IRateLimitCounter
{
    private const string Script = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        return count
        """;

    public async Task<long> IncrementAsync(string key, TimeSpan window, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await redis.GetDatabase().ScriptEvaluateAsync(
            Script,
            [new RedisKey(key)],
            [new RedisValue(((long)window.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture))]);
        return (long)result;
    }
}
