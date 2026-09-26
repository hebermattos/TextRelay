using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sms.Infrastructure.Observability;

public static class TextRelayTelemetry
{
    public const string ActivitySourceName = "TextRelay";
    public const string MeterName = "TextRelay";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> SmsQueued = Meter.CreateCounter<long>("sms.queued");
    public static readonly Counter<long> SmsSent = Meter.CreateCounter<long>("sms.sent");
    public static readonly Counter<long> SmsFailed = Meter.CreateCounter<long>("sms.failed");
    public static readonly Counter<long> SmsClaimRejected = Meter.CreateCounter<long>("sms.queue.claim.rejected");
    public static readonly Counter<long> QueuePublishFailed = Meter.CreateCounter<long>("sms.queue.publish.failed");
    public static readonly Histogram<double> ProviderDuration = Meter.CreateHistogram<double>("sms.provider.duration", "ms");
    public static readonly Histogram<double> ProcessingDuration = Meter.CreateHistogram<double>("sms.processing.duration", "ms");
}
