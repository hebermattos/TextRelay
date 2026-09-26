namespace Sms.Application.Messages;

public interface ISmsWebhookUrlProvider
{
    Uri GetUrl(string relativePath);
}
