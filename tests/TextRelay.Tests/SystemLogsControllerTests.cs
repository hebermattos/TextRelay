using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Auth;
using Sms.Api.Controllers;
using Sms.Application.Logs;

namespace Sms.Infrastructure.Tests;

public sealed class SystemLogsControllerTests
{
    [Fact]
    public async Task Get_ClampsPageSizeAndQueriesSystemLogs()
    {
        var repository = new RecordingRepository();
        var controller = new SystemLogsController(repository);

        Assert.IsType<OkObjectResult>(await controller.Get(take: 1000));
        Assert.Equal(200, repository.Take);
    }

    [Fact]
    public async Task Get_RejectsInvalidPaginationAndDateRange()
    {
        var controller = new SystemLogsController(new RecordingRepository());
        Assert.IsType<BadRequestObjectResult>(await controller.Get(cursorTimestamp: DateTimeOffset.UtcNow));
        Assert.IsType<BadRequestObjectResult>(await controller.Get(
            from: DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
            to: DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
    }

    [Fact]
    public void Controller_RequiresPlatformAdministratorPolicy()
    {
        var attribute = Assert.Single(typeof(SystemLogsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(PortalSecurity.AdminPolicy, attribute.Policy);
    }

    private sealed class RecordingRepository : ILogEntryRepository
    {
        public int Take { get; private set; }

        public Task<IReadOnlyList<LogEntry>> GetActivityAsync(Guid tenantId, DateTimeOffset? from, DateTimeOffset? to, LogCursor? cursor, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LogEntry>>([]);

        public Task<IReadOnlyList<LogEntry>> GetSystemAsync(DateTimeOffset? from, DateTimeOffset? to, LogCursor? cursor, int take, CancellationToken cancellationToken = default)
        {
            Take = take;
            return Task.FromResult<IReadOnlyList<LogEntry>>([]);
        }
    }
}
