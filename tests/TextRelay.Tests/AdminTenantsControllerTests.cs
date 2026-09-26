using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Controllers;
using Sms.Application.Auth;
using Sms.Application.Tenants;

namespace Sms.Infrastructure.Tests;

public sealed class AdminTenantsControllerTests
{
    [Fact]
    public void ControllerRequiresPlatformAdministratorPolicy()
    {
        var attribute = typeof(AdminTenantsController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Single();

        Assert.Equal(Sms.Api.Auth.PortalSecurity.AdminPolicy, attribute.Policy);
    }

    [Fact]
    public async Task CreateRejectsBlankTenantName()
    {
        var controller = CreateController();

        Assert.IsType<BadRequestObjectResult>(
            await controller.Create(new CreateTenantRequest(" ", null), default));
    }

    [Fact]
    public async Task CreateReturnsProvisionedTenantAndOneTimeSecret()
    {
        var provisioner = new Provisioner();
        var controller = CreateController(provisioner);

        var result = Assert.IsType<CreatedResult>(
            await controller.Create(new CreateTenantRequest("Tenant", "client"), default));

        Assert.Equal($"/api/v1/admin/tenants/{provisioner.TenantId}", result.Location);
        Assert.NotNull(result.Value);
        Assert.Equal("Tenant", provisioner.Name);
        Assert.Equal("client", provisioner.ClientId);
    }

    private static AdminTenantsController CreateController(Provisioner? provisioner = null)
    {
        var service = new TenantProvisioningService(provisioner ?? new Provisioner());
        return new AdminTenantsController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private sealed class Provisioner : ITenantProvisioner
    {
        public Guid TenantId { get; private set; }
        public string? Name { get; private set; }
        public string? ClientId { get; private set; }

        public Task CreateAsync(Guid id, string name, CreateApiClient client, CancellationToken cancellationToken = default)
        {
            TenantId = id;
            Name = name;
            ClientId = client.ClientId;
            return Task.CompletedTask;
        }
    }
}
