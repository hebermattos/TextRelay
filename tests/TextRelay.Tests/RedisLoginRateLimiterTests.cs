using Moq;
using StackExchange.Redis;
using Sms.Api.RateLimiting;

namespace Sms.Infrastructure.Tests;

public sealed class RedisLoginRateLimiterTests
{
    [Fact]
    public async Task GetRetryAfterAsync_ReturnsRemainingBlockTime()
    {
        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.KeyTimeToLiveAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(TimeSpan.FromSeconds(90));
        var limiter = CreateLimiter(database);

        var retryAfter = await limiter.GetRetryAfterAsync("203.0.113.10");

        Assert.Equal(TimeSpan.FromSeconds(90), retryAfter);
        database.Verify(
            x => x.KeyTimeToLiveAsync(
                It.Is<RedisKey>(key => key.ToString() == "login-rate-limit:203.0.113.10:blocked"),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task GetRetryAfterAsync_ReturnsNullWhenIpIsNotBlocked()
    {
        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.KeyTimeToLiveAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((TimeSpan?)null);
        var limiter = CreateLimiter(database);

        var retryAfter = await limiter.GetRetryAfterAsync("203.0.113.11");

        Assert.Null(retryAfter);
    }

    [Fact]
    public async Task ResetAsync_DeletesFailureAndBlockState()
    {
        RedisKey[]? deletedKeys = null;
        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey[], CommandFlags>((keys, _) => deletedKeys = keys)
            .ReturnsAsync(2);
        var limiter = CreateLimiter(database);

        await limiter.ResetAsync("203.0.113.12");

        Assert.NotNull(deletedKeys);
        Assert.Equal(
            new[]
            {
                "login-rate-limit:203.0.113.12:failures",
                "login-rate-limit:203.0.113.12:blocked"
            },
            deletedKeys!.Select(key => key.ToString()).ToArray());
    }

    [Fact]
    public async Task RedisOperations_HonorCancellationBeforeAccessingRedis()
    {
        var database = new Mock<IDatabase>();
        var limiter = CreateLimiter(database);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => limiter.GetRetryAfterAsync("203.0.113.13", source.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => limiter.RecordFailureAsync("203.0.113.13", source.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => limiter.ResetAsync("203.0.113.13", source.Token));

        database.VerifyNoOtherCalls();
    }

    private static RedisLoginRateLimiter CreateLimiter(Mock<IDatabase> database)
    {
        var redis = new Mock<IConnectionMultiplexer>();
        redis
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);
        return new RedisLoginRateLimiter(redis.Object);
    }
}
