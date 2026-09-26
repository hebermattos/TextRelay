using Microsoft.AspNetCore.Mvc;
using Sms.Api.Controllers;
using Sms.Application.Auth;
using Sms.Application.Common;

namespace Sms.Infrastructure.Tests;

public sealed class UserControllerBehaviorTests
{
    private const string Password = "StrongPassword123!";

    [Fact]
    public async Task PlatformController_CoversUserOperationsAndPagination()
    {
        var repository = new PlatformRepository();
        var controller = new PlatformUsersController(new PortalUserManagementService(repository));
        await Assert.ThrowsAsync<ArgumentException>(() => controller.List(skip: -1));
        Assert.Empty(await controller.List(take: 500));
        var created = Assert.IsType<CreatedResult>(await controller.Create(new("platform.user", "platform@example.com", Password, "administrator"), default));
        Assert.StartsWith("/api/v1/admin/platform-users/", created.Location);
        var id = repository.User!.Id;
        Assert.IsType<NoContentResult>(await controller.SetState(id, new(false), default));
        Assert.IsType<NoContentResult>(await controller.ResetPassword(id, new(Password), default));
    }

    [Fact]
    public async Task TenantController_CoversUserOperationsAndPagination()
    {
        var tenantId = Guid.NewGuid();
        var repository = new TenantRepository();
        var controller = new TenantUsersController(new TenantContext(tenantId), new TenantPortalUserManagementService(repository));
        await Assert.ThrowsAsync<ArgumentException>(() => controller.List(skip: -1));
        Assert.Empty(await controller.List(take: 500));
        var created = Assert.IsType<CreatedResult>(await controller.Create(new("tenant.user", "tenant@example.com", Password, "user"), default));
        Assert.StartsWith("/api/v1/tenant/users/", created.Location);
        var id = repository.User!.Id;
        Assert.IsType<NoContentResult>(await controller.Update(id, new("changed.user", "changed@example.com", "administrator"), default));
        Assert.IsType<NoContentResult>(await controller.SetState(id, new(false), default));
        Assert.IsType<NoContentResult>(await controller.ResetPassword(id, new(Password), default));
    }

    private sealed class TenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
        public string? UserId => null;
    }

    private sealed class PlatformRepository : IPortalUserManagementRepository
    {
        public NewPortalUser? User { get; private set; }
        public Task<IReadOnlyList<PortalUserSummary>> ListPlatformUsersAsync(int skip = 0, int take = 20, string? search = null, string? role = null, bool? isActive = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PortalUserSummary>>([]);
        public Task<Guid> CreatePlatformUserAsync(NewPortalUser user, CancellationToken cancellationToken = default) { User = user; return Task.FromResult(user.Id); }
        public Task<bool> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default) => Task.FromResult(User?.Id == id);
        public Task<bool> ResetPasswordAsync(Guid id, byte[] hash, byte[] salt, int iterations, CancellationToken cancellationToken = default) => Task.FromResult(User?.Id == id);
    }

    private sealed class TenantRepository : ITenantPortalUserManagementRepository
    {
        public NewPortalUser? User { get; private set; }
        public Task<IReadOnlyList<PortalUserSummary>> ListAsync(Guid tenantId, int skip = 0, int take = 20, string? search = null, string? role = null, bool? isActive = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PortalUserSummary>>([]);
        public Task<Guid> CreateAsync(NewPortalUser user, CancellationToken cancellationToken = default) { User = user; return Task.FromResult(user.Id); }
        public Task<bool> UpdateAsync(Guid tenantId, Guid id, string username, string email, string role, CancellationToken cancellationToken = default) { if (User?.Id != id || User.TenantId != tenantId) return Task.FromResult(false); User = User with { Username = username, Email = email, Role = role }; return Task.FromResult(true); }
        public Task<bool> SetActiveAsync(Guid tenantId, Guid id, bool isActive, CancellationToken cancellationToken = default) => Task.FromResult(User?.Id == id && User.TenantId == tenantId);
        public Task<bool> ResetPasswordAsync(Guid tenantId, Guid id, byte[] hash, byte[] salt, int iterations, CancellationToken cancellationToken = default) => Task.FromResult(User?.Id == id && User.TenantId == tenantId);
    }
}
