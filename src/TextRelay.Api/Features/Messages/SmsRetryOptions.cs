namespace Sms.Application.Messages;

public sealed class SmsRetryOptions
{
    public const int DefaultMaxAttempts = 3;
    public const int DefaultInitialIntervalSeconds = 60;

    public int MaxAttempts { get; init; } = DefaultMaxAttempts;
    public int InitialIntervalSeconds { get; init; } = DefaultInitialIntervalSeconds;

    public void Validate()
    {
        if (MaxAttempts is < 0 or > 10)
            throw new InvalidOperationException("SMS retry max attempts must be between 0 and 10.");
        if (InitialIntervalSeconds is < 1 or > 86400)
            throw new InvalidOperationException("SMS retry initial interval must be between 1 and 86400 seconds.");
    }
}
