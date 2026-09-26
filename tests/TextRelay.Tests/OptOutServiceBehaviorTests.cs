using Sms.Application.OptOut;

namespace Sms.Infrastructure.Tests;

public sealed class OptOutServiceBehaviorTests
{
    private readonly Guid tenantId = Guid.NewGuid();

    [Fact]
    public async Task List_ValidatesAndClampsPagination()
    {
        var repository = new Repository();
        var service = new OptOutService(repository);
        await service.ListAsync(tenantId, 2, 500);
        Assert.Equal(2, repository.Skip);
        Assert.Equal(200, repository.Take);
        await service.ListAsync(tenantId, 0, 0);
        Assert.Equal(1, repository.Take);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(tenantId, -1, 20));
    }

    [Fact]
    public async Task Add_NormalizesPhoneAndCleansReason()
    {
        var repository = new Repository();
        var service = new OptOutService(repository);
        await service.AddAsync(tenantId, "+1 (555) 123-4567", "  requested  ");
        Assert.Equal("+15551234567", repository.Phone);
        Assert.Equal("Manual", repository.Source);
        Assert.Equal("requested", repository.Reason);
        await service.AddAsync(tenantId, "5551234567", " ");
        Assert.Null(repository.Reason);
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(tenantId, "5551234567", new string('x', 201)));
    }

    [Fact]
    public async Task Import_ProcessesRowsAndRejectsMoreThanLimit()
    {
        var repository = new Repository();
        var service = new OptOutService(repository);
        await service.ImportAsync(tenantId, [new("5551234567", " one "), new("5551234568")]);
        Assert.Equal(2, repository.AddCount);
        Assert.Equal("Import", repository.Source);
        var tooMany = Enumerable.Range(0, 1001).Select(_ => new AddBlockedNumber("5551234567"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ImportAsync(tenantId, tooMany));
    }

    [Fact]
    public async Task EnsureCanSend_ThrowsForBlockedRecipient()
    {
        var repository = new Repository();
        var service = new OptOutService(repository);
        await service.EnsureCanSendAsync(tenantId, "5551234567");
        repository.Blocked = true;
        await Assert.ThrowsAsync<BlockedRecipientException>(() => service.EnsureCanSendAsync(tenantId, "5551234567"));
    }

    [Theory]
    [InlineData("STOP")]
    [InlineData(" unsubscribe ")]
    [InlineData("cancel")]
    public async Task StopKeywords_BlockRecipient(string keyword)
    {
        var repository = new Repository();
        var service = new OptOutService(repository);
        var occurredAt = DateTimeOffset.UtcNow;
        await service.ProcessInboundAsync(tenantId, "5551234567", keyword, occurredAt);
        Assert.Equal("InboundKeyword", repository.Source);
        Assert.Equal(keyword.Trim().ToUpperInvariant(), repository.Reason);
        Assert.Equal(occurredAt, repository.OccurredAt);
    }

    [Fact]
    public async Task Start_RemovesAndOtherTextDoesNothing()
    {
        var repository = new Repository();
        var service = new OptOutService(repository);
        await service.ProcessInboundAsync(tenantId, "5551234567", " START ", DateTimeOffset.UtcNow);
        Assert.Equal(1, repository.RemoveByPhoneCount);
        await service.ProcessInboundAsync(tenantId, "5551234567", "hello", DateTimeOffset.UtcNow);
        Assert.Equal(1, repository.RemoveByPhoneCount);
    }

    [Fact]
    public async Task Remove_DelegatesToRepository()
    {
        var repository = new Repository { RemoveResult = true };
        Assert.True(await new OptOutService(repository).RemoveAsync(tenantId, Guid.NewGuid()));
    }

    private sealed class Repository : IOptOutRepository
    {
        public int Skip { get; private set; }
        public int Take { get; private set; }
        public bool Blocked { get; set; }
        public bool RemoveResult { get; set; }
        public string? Phone { get; private set; }
        public string? Source { get; private set; }
        public string? Reason { get; private set; }
        public DateTimeOffset OccurredAt { get; private set; }
        public int AddCount { get; private set; }
        public int RemoveByPhoneCount { get; private set; }
        public Task<IReadOnlyList<BlockedNumber>> ListAsync(Guid tenantId, int skip, int take, CancellationToken cancellationToken = default) { Skip = skip; Take = take; return Task.FromResult<IReadOnlyList<BlockedNumber>>([]); }
        public Task<bool> IsBlockedAsync(Guid tenantId, string phoneNumber, CancellationToken cancellationToken = default) { Phone = phoneNumber; return Task.FromResult(Blocked); }
        public Task AddOrUpdateAsync(Guid tenantId, string phoneNumber, string source, string? reason, DateTimeOffset occurredAt, CancellationToken cancellationToken = default) { Phone = phoneNumber; Source = source; Reason = reason; OccurredAt = occurredAt; AddCount++; return Task.CompletedTask; }
        public Task<bool> RemoveAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(RemoveResult);
        public Task RemoveByPhoneAsync(Guid tenantId, string phoneNumber, CancellationToken cancellationToken = default) { Phone = phoneNumber; RemoveByPhoneCount++; return Task.CompletedTask; }
    }
}
