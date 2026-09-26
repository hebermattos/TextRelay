using Moq;
using StackExchange.Redis;
using Sms.Api.RateLimiting;

namespace Sms.Infrastructure.Tests;

public sealed class RedisRateLimitCounterTests
{
    [Fact]
    public async Task IncrementAsync_EvaluatesAtomicRedisScriptAndReturnsCount()
    {
        var database = new Mock<IDatabase>();
        RedisKey[]? keys = null;
        RedisValue[]? values = null;
        database
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((_, capturedKeys, capturedValues, _) =>
            {
                keys = capturedKeys;
                values = capturedValues;
            })
            .ReturnsAsync(RedisResult.Create((RedisValue)2L));

        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

        var counter = new RedisRateLimitCounter(redis.Object);

        var result = await counter.IncrementAsync("rate-limit:test", TimeSpan.FromSeconds(60));

        Assert.Equal(2, result);
        Assert.NotNull(keys);
        Assert.Single(keys!);
        Assert.Equal("rate-limit:test", keys![0].ToString());
        Assert.NotNull(values);
        Assert.Single(values!);
        Assert.Equal("60000", values![0].ToString());
    }

    [Fact]
    public async Task IncrementAsync_WhenCancelled_DoesNotCallRedis()
    {
        var redis = new Mock<IConnectionMultiplexer>();
        var counter = new RedisRateLimitCounter(redis.Object);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            counter.IncrementAsync("rate-limit:test", TimeSpan.FromMinutes(1), cancellation.Token));

        redis.Verify(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()), Times.Never);
    }
}
