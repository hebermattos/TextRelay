using System.Text;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Controllers;
using Sms.Application.Common;
using Sms.Application.OptOut;

namespace Sms.Infrastructure.Tests;

public sealed class OptOutsControllerTests
{
    [Fact]
    public async Task ListAddImportAndRemove_DelegateWithinTenant()
    {
        var tenant = new Tenant();
        var repository = new Repository();
        var controller = new OptOutsController(tenant, new OptOutService(repository));

        Assert.Empty(await controller.List(2, 30));
        Assert.Equal((tenant.TenantId, 2, 30), (repository.TenantId, repository.Skip, repository.Take));

        Assert.IsType<NoContentResult>(await controller.Add(new("+1 (555) 123-4567", " requested "), default));
        Assert.Equal("+15551234567", repository.Phone);

        Assert.IsType<NoContentResult>(await controller.Import([new("5551234568")], default));
        Assert.Equal(2, repository.AddCount);

        repository.RemoveResult = true;
        Assert.IsType<NoContentResult>(await controller.Remove(Guid.NewGuid(), default));
        repository.RemoveResult = false;
        Assert.IsType<NotFoundResult>(await controller.Remove(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Export_ReturnsCsvAndEscapesValues()
    {
        var tenant = new Tenant();
        var created = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        var repository = new Repository
        {
            Rows =
            [
                new BlockedNumber(Guid.NewGuid(), "+15551234567", "Manual", "asked, \"please\"", created, null)
            ]
        };
        var controller = new OptOutsController(tenant, new OptOutService(repository));

        var result = Assert.IsType<FileContentResult>(await controller.Export(default));
        var csv = Encoding.UTF8.GetString(result.FileContents);

        Assert.Equal("text/csv; charset=utf-8", result.ContentType);
        Assert.Equal("sms-opt-outs.csv", result.FileDownloadName);
        Assert.Contains("PhoneNumber,Source,Reason,CreatedAt", csv);
        Assert.Contains("\"asked, \"\"please\"\"\"", csv);
        Assert.Contains(created.ToString("O"), csv);
    }

    private sealed class Tenant : ITenantContext
    {
        public Guid TenantId { get; } = Guid.NewGuid();
    }

    private sealed class Repository : IOptOutRepository
    {
        public Guid TenantId { get; private set; }
        public int Skip { get; private set; }
        public int Take { get; private set; }
        public string? Phone { get; private set; }
        public int AddCount { get; private set; }
        public bool RemoveResult { get; set; }
        public IReadOnlyList<BlockedNumber> Rows { get; set; } = [];

        public Task<IReadOnlyList<BlockedNumber>> ListAsync(Guid tenantId, int skip, int take, CancellationToken cancellationToken = default)
        {
            TenantId = tenantId; Skip = skip; Take = take;
            return Task.FromResult<IReadOnlyList<BlockedNumber>>(Rows.Skip(skip).Take(take).ToArray());
        }

        public Task<bool> IsBlockedAsync(Guid tenantId, string phoneNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task AddOrUpdateAsync(Guid tenantId, string phoneNumber, string source, string? reason, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
        {
            TenantId = tenantId; Phone = phoneNumber; AddCount++;
            return Task.CompletedTask;
        }

        public Task<bool> RemoveAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
        {
            TenantId = tenantId;
            return Task.FromResult(RemoveResult);
        }

        public Task RemoveByPhoneAsync(Guid tenantId, string phoneNumber, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
