using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Messages;
using Sms.Domain.Messages;
using Sms.Infrastructure.Providers;

namespace Sms.Api.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/v1/webhooks/bandwidth")]
[RequestSizeLimit(1_048_576)]
public sealed class BandwidthWebhooksController(
    ReceiveSmsWebhookService service, BandwidthWebhookParser parser) : ControllerBase
{
    [HttpPost("inbound")]
    public Task<IActionResult> Inbound(CancellationToken cancellationToken) =>
        ReceiveAsync(SmsDirection.Inbound, cancellationToken);

    [HttpPost("status")]
    public Task<IActionResult> Status(CancellationToken cancellationToken) =>
        ReceiveAsync(SmsDirection.Outbound, cancellationToken);

    private async Task<IActionResult> ReceiveAsync(SmsDirection direction, CancellationToken cancellationToken)
    {
        // Bandwidth can first send a request without credentials and wait for a Basic challenge.
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization)) return ChallengeBasic();
        if (!Request.HasJsonContentType()) return StatusCode(StatusCodes.Status415UnsupportedMediaType);

        var result = await service.ReceiveAsync(parser, authorization, Request.Body, direction, cancellationToken);
        return result switch
        {
            SmsWebhookResult.Accepted => NoContent(),
            SmsWebhookResult.Unauthorized => ChallengeBasic(),
            _ => BadRequest()
        };
    }

    private IActionResult ChallengeBasic()
    {
        Response.Headers.WWWAuthenticate = "Basic realm=\"Bandwidth webhooks\", charset=\"UTF-8\"";
        return Unauthorized();
    }
}
