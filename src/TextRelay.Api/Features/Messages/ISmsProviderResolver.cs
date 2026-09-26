namespace Sms.Api.Features.Messages;

public interface ISmsProviderResolver
{
    ISmsProvider Resolve(string? provider = null);
}
