using Sms.Application.OptOut;

namespace Sms.Infrastructure.Tests;

public sealed class OptOutServiceTests
{
    [Fact]
    public async Task StopAndStart_BlockAndRestoreSendingForTenant()
    {
        var tenantId = Guid.NewGuid();
        var repository = new TestOptOutRepository();
        var service = new OptOutService(repository);

        await service.ProcessInboundAsync(tenantId, "+1 (555) 123-4567", " stop ", DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<BlockedRecipientException>(() => service.EnsureCanSendAsync(tenantId, "+15551234567"));

        await service.ProcessInboundAsync(tenantId, "+15551234567", "START", DateTimeOffset.UtcNow);
        await service.EnsureCanSendAsync(tenantId, "+15551234567");
    }

    [Fact]
    public async Task BlockedNumber_IsIsolatedByTenant()
    {
        var repository = new TestOptOutRepository();
        var service = new OptOutService(repository);
        var firstTenant = Guid.NewGuid();

        await service.AddAsync(firstTenant, "+15551234567", "requested");

        await Assert.ThrowsAsync<BlockedRecipientException>(() => service.EnsureCanSendAsync(firstTenant, "+15551234567"));
        await service.EnsureCanSendAsync(Guid.NewGuid(), "+15551234567");
    }

    [Theory]
    [InlineData("STOP")]
    [InlineData("unsubscribe")]
    [InlineData(" Cancel ")]
    public async Task SupportedStopWords_BlockNumber(string command)
    {
        var tenantId = Guid.NewGuid();
        var service = new OptOutService(new TestOptOutRepository());
        await service.ProcessInboundAsync(tenantId, "+15551234567", command, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<BlockedRecipientException>(() => service.EnsureCanSendAsync(tenantId, "+15551234567"));
    }

    [Fact]
    public async Task Import_RejectsMoreThanOneThousandRows()
    {
        var service = new OptOutService(new TestOptOutRepository());
        var rows = Enumerable.Range(0, 1001).Select(_ => new AddBlockedNumber("+15551234567"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ImportAsync(Guid.NewGuid(), rows));
    }
}
