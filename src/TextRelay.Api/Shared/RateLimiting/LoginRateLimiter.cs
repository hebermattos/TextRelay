using System.Globalization;
using StackExchange.Redis;

namespace Sms.Api.RateLimiting;

public sealed record LoginFailureState(long FailureCount, TimeSpan? RetryAfter);

public interface ILoginRateLimiter
{
    Task<TimeSpan?> GetRetryAfterAsync(string ipAddress, CancellationToken cancellationToken = default);
    Task<LoginFailureState> RecordFailureAsync(string ipAddress, CancellationToken cancellationToken = default);
    Task ResetAsync(string ipAddress, CancellationToken cancellationToken = default);
}

internal static class LoginRateLimitPolicy
{
    internal const int AllowedFailures = 3;
    internal static readonly TimeSpan FailureRetention = TimeSpan.FromHours(1);
    internal static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(4),
        TimeSpan.FromMinutes(8),
        TimeSpan.FromMinutes(10)
    ];

    internal static TimeSpan? GetDelay(long failureCount)
    {
        if (failureCount < AllowedFailures) return null;

        var index = (int)Math.Min(failureCount - AllowedFailures, Delays.Length - 1);
        return Delays[index];
    }
}

public sealed class RedisLoginRateLimiter(IConnectionMultiplexer redis) : ILoginRateLimiter
{
    private const string RecordFailureScript = """
        local count = redis.call('INCR', KEYS[1])
        redis.call('PEXPIRE', KEYS[1], ARGV[1])

        local threshold = tonumber(ARGV[2])
        if count < threshold then
            return { count, 0 }
        end

        local delayIndex = math.min(count - threshold + 3, #ARGV)
        local delay = tonumber(ARGV[delayIndex])
        local currentTtl = redis.call('PTTL', KEYS[2])
        if currentTtl < delay then
            redis.call('PSETEX', KEYS[2], delay, '1')
        else
            delay = currentTtl
        end

        return { count, delay }
        """;

    public async Task<TimeSpan?> GetRetryAfterAsync(
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ttl = await redis.GetDatabase().KeyTimeToLiveAsync(BlockKey(ipAddress));
        return ttl is { } value && value > TimeSpan.Zero ? value : null;
    }

    public async Task<LoginFailureState> RecordFailureAsync(
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var arguments = new RedisValue[2 + LoginRateLimitPolicy.Delays.Length];
        arguments[0] = Milliseconds(LoginRateLimitPolicy.FailureRetention);
        arguments[1] = LoginRateLimitPolicy.AllowedFailures;
        for (var i = 0; i < LoginRateLimitPolicy.Delays.Length; i++)
            arguments[i + 2] = Milliseconds(LoginRateLimitPolicy.Delays[i]);

        var result = (RedisResult[])(await redis.GetDatabase().ScriptEvaluateAsync(
            RecordFailureScript,
            [new RedisKey(FailureKey(ipAddress)), new RedisKey(BlockKey(ipAddress))],
            arguments))!;

        var failureCount = (long)result[0];
        var delayMilliseconds = (long)result[1];
        return new LoginFailureState(
            failureCount,
            delayMilliseconds > 0 ? TimeSpan.FromMilliseconds(delayMilliseconds) : null);
    }

    public async Task ResetAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await redis.GetDatabase().KeyDeleteAsync(
            [new RedisKey(FailureKey(ipAddress)), new RedisKey(BlockKey(ipAddress))]);
    }

    private static string FailureKey(string ipAddress) => $"login-rate-limit:{ipAddress}:failures";
    private static string BlockKey(string ipAddress) => $"login-rate-limit:{ipAddress}:blocked";
    private static RedisValue Milliseconds(TimeSpan value) =>
        ((long)value.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
}
