namespace Sms.Api.Features.Messages;

public sealed class TransientSmsProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);
