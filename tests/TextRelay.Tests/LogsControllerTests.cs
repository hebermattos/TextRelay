using Microsoft.AspNetCore.Mvc;
using Sms.Api.Controllers;
using Sms.Application.Common;
using Sms.Application.Logs;

namespace Sms.Infrastructure.Tests;

public sealed class LogsControllerTests
{
    [Fact]
    public async Task Get_UsesAuthenticatedTenantAndClampsPageSize()
    {
        var tenantId = Guid.NewGuid();
        var repository = new RecordingRepository();
        var controller = new LogsController(new TenantContext(tenantId), repository);

        var result = await controller.Get(take: 1000);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(tenantId, repository.TenantId);
        Assert.Equal(200, repository.Take);
    }

    [Fact]
    public async Task Get_RejectsInvalidPaginationAndDateRange()
    {
        var controller = new LogsController(new TenantContext(Guid.NewGuid()), new RecordingRepository());
        Assert.IsType<BadRequestObjectResult>(await controller.Get(cursorTimestamp: DateTimeOffset.UtcNow));
        Assert.IsType<BadRequestObjectResult>(await controller.Get(
            from: DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
            to: DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
    }

    private sealed record TenantContext(Guid TenantId) : ITenantContext;

    private sealed class RecordingRepository : ILogEntryRepository
    {
        public Guid TenantId { get; private set; }
        public int Take { get; private set; }

        public Task<IReadOnlyList<LogEntry>> GetActivityAsync(Guid tenantId, DateTimeOffset? from, DateTimeOffset? to, LogCursor? cursor, int take, CancellationToken cancellationToken = default)
        {
            TenantId = tenantId;
            Take = take;
            return Task.FromResult<IReadOnlyList<LogEntry>>([]);
        }

        public Task<IReadOnlyList<LogEntry>> GetSystemAsync(DateTimeOffset? from, DateTimeOffset? to, LogCursor? cursor, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LogEntry>>([]);
    }
}
