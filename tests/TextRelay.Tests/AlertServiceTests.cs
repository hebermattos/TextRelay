using Sms.Application.Alerts;
using Sms.Domain.Messages;

namespace Sms.Infrastructure.Tests;

public sealed class AlertServiceTests
{
    [Fact]
    public async Task CreateRule_NormalizesAndPersistsTenantRule()
    {
        var repository = new FakeAlertRepository();
        var tenantId = Guid.NewGuid();
        var service = new AlertService(repository, new AlertRuleFactory(TimeProvider.System));

        var id = await service.CreateRuleAsync(tenantId, new(" Failure spike ", " Twilio ", SmsStatus.Failed, 15, true));

        Assert.Equal(id, repository.Rule!.Id);
        Assert.Equal(tenantId, repository.Rule.TenantId);
        Assert.Equal("Failure spike", repository.Rule.Name);
        Assert.Equal("Twilio", repository.Rule.Provider);
        Assert.Equal(15, repository.Rule.WindowMinutes);
    }

    [Theory]
    [InlineData("", 15)]
    [InlineData("Rule", 0)]
    [InlineData("Rule", 1441)]
    public async Task CreateRule_RejectsInvalidConfiguration(string name, int window)
    {
        var service = new AlertService(new FakeAlertRepository(), new AlertRuleFactory(TimeProvider.System));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRuleAsync(Guid.NewGuid(),
            new(name, null, SmsStatus.Failed, window, true)));
    }

    [Fact]
    public async Task CreateRule_RejectsUndefinedStatus()
    {
        var service = new AlertService(new FakeAlertRepository(), new AlertRuleFactory(TimeProvider.System));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRuleAsync(Guid.NewGuid(),
            new("Invalid", null, (SmsStatus)99, 5, true)));
    }

    [Fact]
    public async Task MissingTenantOwnedRecords_AreNotModified()
    {
        var service = new AlertService(new FakeAlertRepository(), new AlertRuleFactory(TimeProvider.System));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateRuleAsync(Guid.NewGuid(), Guid.NewGuid(),
            new("Rule", null, SmsStatus.Failed, 5, true)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteRuleAsync(Guid.NewGuid(), Guid.NewGuid()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.MarkReadAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    private sealed class FakeAlertRepository : IAlertRepository
    {
        public AlertRule? Rule { get; private set; }
        public Task<IReadOnlyList<AlertRule>> ListRulesAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AlertRule>>([]);
        public Task CreateRuleAsync(AlertRule rule, CancellationToken cancellationToken = default) { Rule = rule; return Task.CompletedTask; }
        public Task<bool> UpdateRuleAsync(AlertRule rule, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> DeleteRuleAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<AlertNotification>> ListAlertsAsync(Guid tenantId, bool unreadOnly, int skip, int take, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AlertNotification>>([]);
        public Task<bool> MarkReadAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task MarkAllReadAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AlertRule?> GetRuleAsync(Guid tenantId, Guid ruleId, CancellationToken cancellationToken = default) => Task.FromResult<AlertRule?>(Rule);
        public Task EvaluateRuleAsync(Guid eventId, Guid ruleId, Guid tenantId, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
