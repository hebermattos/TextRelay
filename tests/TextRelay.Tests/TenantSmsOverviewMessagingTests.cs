using Microsoft.Extensions.Logging.Abstractions;
using Sms.Infrastructure.Messaging;

namespace Sms.Infrastructure.Tests;

public sealed class TenantSmsOverviewMessagingTests
{
    [Fact]
    public async Task Consumer_DelegatesEventToProjection()
    {
        var projection = new Projection();
        var consumer = new TenantSmsOverviewConsumer(projection);
        var item = CreateEvent();

        await consumer.ApplyAsync(item);

        Assert.Same(item, projection.Applied);
        Assert.Equal(item.EventId, projection.Applied!.EventId);
        Assert.Equal(item.TenantId, projection.Applied.TenantId);
        Assert.Equal(item.UserId, projection.Applied.UserId);
        Assert.Equal(item.MessageId, projection.Applied.MessageId);
        Assert.Equal("Tenant", projection.Applied.TenantName);
        Assert.Equal("user", projection.Applied.Username);
        Assert.Equal("Mock", projection.Applied.Provider);
        Assert.Equal(1, projection.Applied.Direction);
        Assert.Equal(2, projection.Applied.QueueStatus);
        Assert.Equal(3, projection.Applied.Status);
        Assert.NotEqual(default, projection.Applied.CreatedAtUtc);
        Assert.Equal(1, item.OutboundDelta);
        Assert.Equal(2, item.InboundDelta);
        Assert.Equal(3, item.DeliveredDelta);
        Assert.Equal(4, item.FailedDelta);
        Assert.Equal(5, item.PendingDelta);
        Assert.NotEqual(default, item.OccurredAtUtc);
    }

    [Fact]
    public async Task Publisher_PublishesPendingEventsBeforeMarkingThem()
    {
        var first = CreateEvent();
        var second = CreateEvent();
        var outbox = new Outbox([first, second]);
        var eventPublisher = new EventPublisher();
        var publisher = new TenantSmsOverviewOutboxPublisher(
            outbox, eventPublisher, NullLogger<TenantSmsOverviewOutboxPublisher>.Instance);

        await publisher.PublishBatchAsync();

        Assert.Equal([first, second], eventPublisher.Published);
        Assert.Equal([first.EventId, second.EventId], outbox.Marked);
    }

    [Fact]
    public async Task BackgroundPublisher_StartsAndStopsWithNoPendingEvents()
    {
        var publisher = new TenantSmsOverviewOutboxPublisher(
            new Outbox([]), new EventPublisher(), NullLogger<TenantSmsOverviewOutboxPublisher>.Instance);
        using var cancellation = new CancellationTokenSource();

        await publisher.StartAsync(cancellation.Token);
        await Task.Delay(25);
        await publisher.StopAsync(default);
    }

    [Fact]
    public async Task BackgroundPublisher_HandlesOutboxFailureUntilStopped()
    {
        var publisher = new TenantSmsOverviewOutboxPublisher(
            new FailingOutbox(), new EventPublisher(), NullLogger<TenantSmsOverviewOutboxPublisher>.Instance);
        using var cancellation = new CancellationTokenSource();

        await publisher.StartAsync(cancellation.Token);
        await Task.Delay(25);
        await publisher.StopAsync(default);
    }

    private static TenantSmsOverviewEvent CreateEvent()
    {
        var now = DateTimeOffset.UtcNow;
        return new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Tenant",
            "user",
            "Mock",
            1,
            2,
            3,
            now,
            1,
            2,
            3,
            4,
            5,
            now);
    }

    private sealed class Projection : ITenantSmsOverviewProjection
    {
        public TenantSmsOverviewEvent? Applied { get; private set; }
        public Task ApplyAsync(TenantSmsOverviewEvent item, CancellationToken cancellationToken = default)
        {
            Applied = item;
            return Task.CompletedTask;
        }
    }

    private sealed class Outbox(IReadOnlyList<TenantSmsOverviewEvent> pending) : ITenantSmsOverviewOutbox
    {
        public List<Guid> Marked { get; } = [];
        public Task<IReadOnlyList<TenantSmsOverviewEvent>> GetPendingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(pending);
        public Task MarkPublishedAsync(Guid eventId, CancellationToken cancellationToken = default)
        {
            Marked.Add(eventId);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingOutbox : ITenantSmsOverviewOutbox
    {
        public Task<IReadOnlyList<TenantSmsOverviewEvent>> GetPendingAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Expected test failure.");

        public Task MarkPublishedAsync(Guid eventId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class EventPublisher : ITenantSmsOverviewEventPublisher
    {
        public List<TenantSmsOverviewEvent> Published { get; } = [];
        public Task PublishAsync(TenantSmsOverviewEvent item, CancellationToken cancellationToken = default)
        {
            Published.Add(item);
            return Task.CompletedTask;
        }
    }
}
