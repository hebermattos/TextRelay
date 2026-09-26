using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Auth;
using Sms.Api.Controllers;
using Sms.Application.Administration;
using Sms.Application.Common;
using Sms.Application.Auth;
using Sms.Application.Messages;

namespace Sms.Infrastructure.Tests;

public sealed class AdministrationControllerTests
{
    [Fact]
    public async Task AdministrativeEndpoints_UsePlatformPolicyAndTenantScopedService()
    {
        var attribute = Assert.Single(typeof(AdministrationController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(PortalSecurity.AdminPolicy, attribute.Policy);
        var repo = new AdministrationFakeRepository(); var controller = new AdministrationController(AdministrationServiceTests.Service(repo), new AiSettingsFake(), new SmsRetryOptions());
        Assert.IsType<OkObjectResult>(await controller.ListTenants());
        Assert.IsType<OkObjectResult>(await controller.GetTenant(repo.Tenant.Id, default));
        Assert.IsType<NoContentResult>(await controller.UpdateTenant(repo.Tenant.Id, new("Company", "UTC", false), default));
        Assert.IsType<OkObjectResult>(await controller.Clients(repo.Tenant.Id));
        Assert.IsType<OkObjectResult>(await controller.CreateClient(repo.Tenant.Id, new("client"), default));
        Assert.IsType<NoContentResult>(await controller.SetClientState(repo.Tenant.Id, repo.ClientId, new(false), default));
        Assert.IsType<OkObjectResult>(await controller.RotateSecret(repo.Tenant.Id, repo.ClientId, default));
        Assert.IsType<OkObjectResult>(await controller.Catalog(default));
        Assert.IsType<OkObjectResult>(await controller.Providers(repo.Tenant.Id, default));
        Assert.IsType<NoContentResult>(await controller.SaveProvider(repo.Tenant.Id, "Twilio", new("account", "+15550000001", true, true, "secret", null), default));
        Assert.Equal(repo.Tenant.Id, repo.SavedProvider!.TenantId);
        Assert.IsType<OkObjectResult>(controller.GetSmsRetry());
        Assert.IsType<OkObjectResult>(await controller.GetRateLimits(repo.Tenant.Id, default));
        Assert.IsType<NoContentResult>(await controller.UpdateRateLimits(repo.Tenant.Id, new(100, 20, 20), default));
        Assert.IsType<OkObjectResult>(await controller.GetAiSettings(repo.Tenant.Id, default));
        Assert.IsType<BadRequestObjectResult>(await controller.UpdateAiSettings(repo.Tenant.Id, new("", "Validate"), default));
        Assert.IsType<BadRequestObjectResult>(await controller.UpdateAiSettings(repo.Tenant.Id, new("Improve", ""), default));
        Assert.IsType<BadRequestObjectResult>(await controller.UpdateAiSettings(repo.Tenant.Id, new(new string('x', 8001), "Validate"), default));
        Assert.IsType<NoContentResult>(await controller.UpdateAiSettings(repo.Tenant.Id, new(" Improve ", " Validate "), default));
        Assert.IsType<OkObjectResult>(controller.TimeZones());
    }

    private sealed class AiSettingsFake : ITenantAiSettingsRepository
    {
        public Task<TenantAiSettings> GetAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(new TenantAiSettings("Improve", "Validate"));
        public Task SaveAsync(Guid tenantId, TenantAiSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }


    [Fact]
    public async Task Overview_UsesOnlyAuthenticatedTenantAndHandlesMissingTenant()
    {
        var tenant = new TenantContext(); var repository = new OverviewRepository(); var controller = new OverviewController(tenant, repository);
        Assert.IsType<NotFoundResult>(await controller.Get(default));
        Assert.Equal(tenant.TenantId, repository.RequestedTenant);
        repository.Value = new("Company", "UTC", 1, 2, 1, 0, 0, []);
        Assert.IsType<OkObjectResult>(await controller.Get(default));
    }

    private sealed class TenantContext : ITenantContext { public Guid TenantId { get; } = Guid.NewGuid(); }
    private sealed class OverviewRepository : ITenantPortalRepository
    {
        public Guid RequestedTenant { get; private set; } public TenantOverview? Value { get; set; }
        public Task<TenantOverview?> GetOverviewAsync(Guid tenantId, CancellationToken cancellationToken) { RequestedTenant = tenantId; return Task.FromResult(Value); }
    }
}
