using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Auth;
using Sms.Api.Filters;
using Sms.Application.Administration;
using Sms.Application.Auth;

namespace Sms.Api.Controllers;

public sealed record UpdateTenantRequest(string Name, string TimeZoneId, bool IsActive);
public sealed record UpdateTenantRateLimitsRequest(int RequestsPerMinute, int SmsPerMinute, int OllamaRequestsPerMinute);
public sealed record UpdateTenantAiSettingsRequest(string ImprovePrompt, string ValidatePrompt);
public sealed record CreateClientRequest(string? ClientId);
public sealed record ClientStateRequest(bool IsActive);

[ApiController]
[Authorize(Policy = PortalSecurity.AdminPolicy)]
[ServiceFilter(typeof(PortalExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/admin")]
public sealed class AdministrationController(AdministrationService service, Sms.Application.Messages.ITenantAiSettingsRepository aiSettings, Sms.Application.Messages.SmsRetryOptions retryOptions) : ControllerBase
{
    [HttpGet("sms-retry")]
    public IActionResult GetSmsRetry() => Ok(new
    {
        retryOptions.MaxAttempts,
        retryOptions.InitialIntervalSeconds,
        Strategy = "Exponential"
    });

    [HttpGet("tenants")]
    public async Task<IActionResult> ListTenants(int skip = 0, int take = 20, CancellationToken cancellationToken = default) =>
        Ok(await service.ListTenantsAsync(skip, take, cancellationToken));

    [HttpGet("tenants/{tenantId:guid}")]
    public async Task<IActionResult> GetTenant(Guid tenantId, CancellationToken cancellationToken) =>
        Ok(await service.GetTenantAsync(tenantId, cancellationToken));

    [HttpPut("tenants/{tenantId:guid}")]
    public async Task<IActionResult> UpdateTenant(Guid tenantId, UpdateTenantRequest request, CancellationToken cancellationToken)
    {
        await service.UpdateTenantAsync(tenantId, request.Name, request.TimeZoneId, request.IsActive, cancellationToken);
        return NoContent();
    }

    [HttpGet("tenants/{tenantId:guid}/rate-limits")]
    public async Task<IActionResult> GetRateLimits(Guid tenantId, CancellationToken cancellationToken) =>
        Ok(await service.GetRateLimitsAsync(tenantId, cancellationToken));

    [HttpPut("tenants/{tenantId:guid}/rate-limits")]
    public async Task<IActionResult> UpdateRateLimits(Guid tenantId, UpdateTenantRateLimitsRequest request, CancellationToken cancellationToken)
    {
        await service.UpdateRateLimitsAsync(tenantId,
            new TenantRateLimitSettings(request.RequestsPerMinute, request.SmsPerMinute, request.OllamaRequestsPerMinute), cancellationToken);
        return NoContent();
    }

    [HttpGet("tenants/{tenantId:guid}/ai-settings")]
    public async Task<IActionResult> GetAiSettings(Guid tenantId, CancellationToken cancellationToken) =>
        Ok(await aiSettings.GetAsync(tenantId, cancellationToken));

    [HttpPut("tenants/{tenantId:guid}/ai-settings")]
    public async Task<IActionResult> UpdateAiSettings(Guid tenantId, UpdateTenantAiSettingsRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ImprovePrompt) || request.ImprovePrompt.Length > 8000 ||
            string.IsNullOrWhiteSpace(request.ValidatePrompt) || request.ValidatePrompt.Length > 8000)
            return BadRequest(new { error = "AI prompts are required and cannot exceed 8000 characters." });
        await service.GetTenantAsync(tenantId, cancellationToken);
        await aiSettings.SaveAsync(tenantId, new(request.ImprovePrompt.Trim(), request.ValidatePrompt.Trim()), cancellationToken);
        return NoContent();
    }

    [HttpGet("time-zones")]
    public IActionResult TimeZones() => Ok(AdministrationService.TimeZones);

    [HttpGet("tenants/{tenantId:guid}/clients")]
    public async Task<IActionResult> Clients(Guid tenantId, int skip = 0, int take = 20, CancellationToken cancellationToken = default) =>
        Ok(await service.ListClientsAsync(tenantId, skip, take, cancellationToken));

    [HttpPost("tenants/{tenantId:guid}/clients")]
    public async Task<IActionResult> CreateClient(Guid tenantId, CreateClientRequest request, CancellationToken cancellationToken) =>
        Ok(await service.CreateClientAsync(tenantId, request.ClientId, cancellationToken));

    [HttpPut("tenants/{tenantId:guid}/clients/{clientId:guid}/state")]
    public async Task<IActionResult> SetClientState(Guid tenantId, Guid clientId, ClientStateRequest request, CancellationToken cancellationToken)
    {
        await service.SetClientActiveAsync(tenantId, clientId, request.IsActive, cancellationToken);
        return NoContent();
    }

    [HttpPost("tenants/{tenantId:guid}/clients/{clientId:guid}/rotate-secret")]
    public async Task<IActionResult> RotateSecret(Guid tenantId, Guid clientId, CancellationToken cancellationToken) =>
        Ok(await service.RotateClientSecretAsync(tenantId, clientId, cancellationToken));

    [HttpGet("providers/catalog")]
    public async Task<IActionResult> Catalog(CancellationToken cancellationToken) =>
        Ok(await service.GetProviderCatalogAsync(cancellationToken));

    [HttpGet("tenants/{tenantId:guid}/providers")]
    public async Task<IActionResult> Providers(Guid tenantId, CancellationToken cancellationToken) =>
        Ok(await service.ListProvidersAsync(tenantId, cancellationToken));

    [HttpPut("tenants/{tenantId:guid}/providers/{provider}")]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> SaveProvider(Guid tenantId, string provider, ProviderEdit request, CancellationToken cancellationToken)
    {
        await service.SaveProviderAsync(tenantId, provider, request, cancellationToken);
        return NoContent();
    }
}
