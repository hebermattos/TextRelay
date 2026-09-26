using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Auth;
using Sms.Application.Messages;

namespace Sms.Api.Controllers;

[ApiController]
[Authorize(Policy = PortalSecurity.TenantPortalPolicy)]
[Route("api/v1/message-assistant")]
[RequestSizeLimit(16_384)]
public sealed class MessageAssistantController(IMessageAssistant assistant, Sms.Application.Common.ITenantContext tenant) : ControllerBase
{
    [HttpPost("improve")]
    public Task<IActionResult> Improve([FromBody] MessageAssistantRequest request, CancellationToken cancellationToken) =>
        ValidateAndExecuteAsync(request, () => assistant.ImproveAsync(tenant.TenantId, request.Message, cancellationToken));

    [HttpPost("validate")]
    public Task<IActionResult> Validate([FromBody] MessageAssistantRequest request, CancellationToken cancellationToken) =>
        ValidateAndExecuteAsync(request, () => assistant.ValidateAsync(tenant.TenantId, request.Message, cancellationToken));

    private Task<IActionResult> ValidateAndExecuteAsync(MessageAssistantRequest request, Func<Task<MessageAssistantResult>> action)
    {
        if (string.IsNullOrWhiteSpace(request.Message)) return Task.FromResult<IActionResult>(BadRequest("Message is required."));
        if (request.Message.Length > 4000) return Task.FromResult<IActionResult>(BadRequest("Message cannot exceed 4000 characters."));
        return ExecuteAsync(action);
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task<MessageAssistantResult>> action)
    {
        try { return Ok(await action()); }
        catch (ArgumentException exception) { return BadRequest(exception.Message); }
    }
}

public sealed record MessageAssistantRequest(string Message);
