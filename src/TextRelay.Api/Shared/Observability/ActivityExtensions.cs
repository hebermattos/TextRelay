using System.Diagnostics;

namespace Sms.Infrastructure.Observability;

public static class ActivityExtensions
{
    public static void RecordException(this Activity activity, Exception exception)
    {
        var tags = new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message }
        };

        activity.AddEvent(new ActivityEvent("exception", tags: tags));
    }
}
