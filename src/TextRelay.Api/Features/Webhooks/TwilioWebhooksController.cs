using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Messages;
using Sms.Application.Providers;
using Sms.Domain.Messages;
using Sms.Infrastructure.Providers;
using Sms.Application.OptOut;

namespace Sms.Api.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/v1/webhooks/twilio")]
[RequestSizeLimit(1_048_576)]
public sealed class TwilioWebhooksController(
    ITenantSmsProviderRepository providers,
    ISmsMessageRepository messages,
    TwilioWebhookValidator validator,
    ISmsWebhookUrlProvider webhookUrls,
    OptOutService optOuts) : ControllerBase
{
    [HttpPost("inbound")]
    public async Task<IActionResult> Inbound(CancellationToken cancellationToken)
    {
        var form = await Request.ReadFormAsync(cancellationToken);
        var config = await ResolveAndValidateAsync(form, form["To"].ToString(), cancellationToken);
        if (config is null) return Forbid();

        var sid = form["MessageSid"].ToString();
        var from = form["From"].ToString();
        var to = form["To"].ToString();
        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) return BadRequest();

        var body = form["Body"].ToString();
        await optOuts.ProcessInboundAsync(config.TenantId, from, body, DateTimeOffset.UtcNow, cancellationToken);

        await messages.InsertInboundIfNotExistsAsync(new SmsMessage
        {
            Id = Guid.NewGuid(), TenantId = config.TenantId, From = from, To = to,
            Body = body, Provider = "Twilio", ProviderMessageId = sid,
            Direction = SmsDirection.Inbound, QueueStatus = SmsQueueStatus.Queued, Status = SmsStatus.Received, CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

        return Content("<Response></Response>", "application/xml");
    }

    [HttpPost("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var form = await Request.ReadFormAsync(cancellationToken);
        var config = await ResolveAndValidateAsync(form, form["From"].ToString(), cancellationToken);
        if (config is null) return Forbid();

        var sid = form["MessageSid"].ToString();
        if (string.IsNullOrWhiteSpace(sid)) return BadRequest();
        var status = MapStatus(form["MessageStatus"].ToString());
        if (!status.HasValue) return BadRequest();
        await messages.UpdateStatusByProviderMessageIdAsync(config.TenantId, "Twilio", sid, status.Value, DateTimeOffset.UtcNow, cancellationToken);
        return NoContent();
    }

    private async Task<TenantSmsProviderConfiguration?> ResolveAndValidateAsync(IFormCollection form, string tenantNumber, CancellationToken cancellationToken)
    {
        var accountSid = form["AccountSid"].ToString();
        if (string.IsNullOrWhiteSpace(accountSid) || string.IsNullOrWhiteSpace(tenantNumber)) return null;
        var config = await providers.GetByAccountAndNumberAsync("Twilio", accountSid, tenantNumber, cancellationToken);
        if (config is null) return null;

        var signature = Request.Headers["X-Twilio-Signature"].ToString();
        var relativeUrl = $"{Request.PathBase}{Request.Path}{Request.QueryString}";
        var url = webhookUrls.GetUrl(relativeUrl).ToString();
        var parameters = form.SelectMany(x => x.Value.Select(value => new KeyValuePair<string, string>(x.Key, value ?? string.Empty)));
        return validator.Validate(url, parameters, signature, config.ApiSecret) ? config : null;
    }

    private static SmsStatus? MapStatus(string status) => status.ToLowerInvariant() switch
    {
        "sent" => SmsStatus.Sent,
        "delivered" => SmsStatus.Delivered,
        "failed" or "undelivered" => SmsStatus.Failed,
        _ => null
    };
}
