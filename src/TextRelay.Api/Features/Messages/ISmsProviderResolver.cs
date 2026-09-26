namespace Sms.Application.Messages;

public interface ISmsProviderResolver
{
    ISmsProvider Resolve(string? provider = null);
}
