namespace Sms.Api.Features.Messages;

public interface ISmsWebhookUrlProvider
{
    Uri GetUrl(string relativePath);
}
