using Sms.Domain.Messages;

namespace Sms.Infrastructure.Tests;

public sealed class SmsStatusHistoryTests
{
    [Fact]
    public void ExposesStatusHistoryValues()
    {
        var id = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var history = new SmsStatusHistory
        {
            Id = id,
            MessageId = messageId,
            Status = SmsStatus.Delivered,
            CreatedAt = createdAt
        };

        Assert.Equal(id, history.Id);
        Assert.Equal(messageId, history.MessageId);
        Assert.Equal(SmsStatus.Delivered, history.Status);
        Assert.Equal(createdAt, history.CreatedAt);
    }
}
