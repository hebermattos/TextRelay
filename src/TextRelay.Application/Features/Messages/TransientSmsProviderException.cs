namespace Sms.Application.Messages;

public sealed class TransientSmsProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);
