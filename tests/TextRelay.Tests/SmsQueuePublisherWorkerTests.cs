using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sms.Domain.Messages;
using Sms.Infrastructure.Messaging;

namespace Sms.Infrastructure.Tests;

public sealed class SmsQueuePublisherWorkerTests
{
    [Fact]
    public async Task PublishBatchAsync_PublishesEveryClaimedMessage()
    {
        var first = Message(SmsQueueStatus.NotQueued);
        var second = Message(SmsQueueStatus.Scheduled);
        var source = new Source([first, second]);
        var bus = new Mock<IBus>();
        var published = new List<SmsSendEvent>();
        bus.Setup(x => x.Publish(It.IsAny<SmsSendEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SmsSendEvent, CancellationToken>((item, _) => published.Add(item))
            .Returns(Task.CompletedTask);
        var worker = CreateWorker(source, bus.Object);

        var count = await worker.PublishBatchAsync();

        Assert.Equal(2, count);
        Assert.Equal([first.MessageId, second.MessageId], published.Select(x => x.MessageId));
        Assert.Equal([first.TenantId, second.TenantId], published.Select(x => x.TenantId));
        Assert.All(published, item => Assert.NotEqual(Guid.Empty, item.EventId));
        Assert.Equal([first.MessageId, second.MessageId], source.Claimed.Select(x => x.MessageId));
        Assert.Empty(source.Released);
    }

    [Fact]
    public async Task PublishBatchAsync_ReleasesFailedPublishAndContinues()
    {
        var first = Message(SmsQueueStatus.NotQueued);
        var second = Message(SmsQueueStatus.Scheduled);
        var source = new Source([first, second]);
        var bus = new Mock<IBus>();
        bus.SetupSequence(x => x.Publish(It.IsAny<SmsSendEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RabbitMQ unavailable"))
            .Returns(Task.CompletedTask);
        var worker = CreateWorker(source, bus.Object);

        var count = await worker.PublishBatchAsync();

        Assert.Equal(1, count);
        Assert.Equal([first.MessageId], source.Released.Select(x => x.MessageId));
        bus.Verify(x => x.Publish(It.IsAny<SmsSendEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task PublishBatchAsync_SkipsMessageClaimedByAnotherWorker()
    {
        var message = Message(SmsQueueStatus.Scheduled);
        var source = new Source([message]) { CanClaim = false };
        var bus = new Mock<IBus>();
        var worker = CreateWorker(source, bus.Object);

        var count = await worker.PublishBatchAsync();

        Assert.Equal(0, count);
        bus.Verify(x => x.Publish(It.IsAny<SmsSendEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishBatchAsync_ReleasesClaimAndPropagatesCancellation()
    {
        var message = Message(SmsQueueStatus.NotQueued);
        var source = new Source([message]);
        using var cancellation = new CancellationTokenSource();
        var bus = new Mock<IBus>();
        bus.Setup(x => x.Publish(It.IsAny<SmsSendEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => cancellation.Cancel())
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));
        var worker = CreateWorker(source, bus.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => worker.PublishBatchAsync(cancellation.Token));

        Assert.Equal([message.MessageId], source.Released.Select(x => x.MessageId));
    }

    [Fact]
    public async Task BackgroundWorker_HandlesSourceFailureUntilStopped()
    {
        var source = new ThrowingSource();
        var worker = CreateWorker(source, Mock.Of<IBus>());

        await worker.StartAsync(default);
        await source.Called.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(default);

        Assert.True(source.Calls >= 1);
    }

    private static SmsQueuePublisherWorker CreateWorker(ISmsQueuePublishSource source, IBus bus) =>
        new(source, bus, NullLogger<SmsQueuePublisherWorker>.Instance);

    private static SmsQueuePublishMessage Message(SmsQueueStatus status) =>
        new(Guid.NewGuid(), Guid.NewGuid(), status, DateTimeOffset.UtcNow.AddMinutes(-10));

    private sealed class ThrowingSource : ISmsQueuePublishSource
    {
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }

        public Task<IReadOnlyList<SmsQueuePublishMessage>> GetPendingAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            Called.TrySetResult();
            throw new InvalidOperationException("database unavailable");
        }

        public Task<bool> TryClaimAsync(SmsQueuePublishMessage message, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task ReleaseAsync(SmsQueuePublishMessage message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class Source(IReadOnlyList<SmsQueuePublishMessage> messages) : ISmsQueuePublishSource
    {
        public List<SmsQueuePublishMessage> Claimed { get; } = [];
        public List<SmsQueuePublishMessage> Released { get; } = [];
        public bool CanClaim { get; init; } = true;

        public Task<IReadOnlyList<SmsQueuePublishMessage>> GetPendingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(messages);

        public Task<bool> TryClaimAsync(SmsQueuePublishMessage message, CancellationToken cancellationToken = default)
        {
            if (!CanClaim)
                return Task.FromResult(false);

            Claimed.Add(message);
            return Task.FromResult(true);
        }

        public Task ReleaseAsync(SmsQueuePublishMessage message, CancellationToken cancellationToken = default)
        {
            Released.Add(message);
            return Task.CompletedTask;
        }
    }
}
