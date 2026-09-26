using Sms.Application.Alerts;
using Sms.Domain.Messages;

namespace Sms.Infrastructure.Tests;

public sealed class AlertServiceBehaviorTests
{
    private readonly Guid tenantId = Guid.NewGuid();

    [Fact]
    public async Task CreateRule_TrimsValuesAndPersistsRule()
    {
        var repository = new Repository();
        var service = new AlertService(repository, new AlertRuleFactory(TimeProvider.System));
        var id = await service.CreateRuleAsync(tenantId,
            new("  Delivery failures  ", " Twilio ", SmsStatus.Failed, 10, true));

        Assert.Equal(id, repository.Rule!.Id);
        Assert.Equal(tenantId, repository.Rule.TenantId);
        Assert.Equal("Delivery failures", repository.Rule.Name);
        Assert.Equal("Twilio", repository.Rule.Provider);
        Assert.Equal(10, repository.Rule.WindowMinutes);
    }

    [Fact]
    public async Task OnceRule_DiscardsRepeatIntervalAndBlankProvider()
    {
        var repository = new Repository();
        await new AlertService(repository, new AlertRuleFactory(TimeProvider.System)).CreateRuleAsync(tenantId,
            new("Rule", " ", SmsStatus.Failed, 1, true));
        Assert.Null(repository.Rule!.Provider);
        Assert.Equal(1, repository.Rule.WindowMinutes);
    }

    [Theory]
    [MemberData(nameof(InvalidRules))]
    public async Task CreateRule_RejectsInvalidRules(SaveAlertRule rule)
    {
        var service = new AlertService(new Repository(), new AlertRuleFactory(TimeProvider.System));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRuleAsync(tenantId, rule));
    }

    public static IEnumerable<object[]> InvalidRules()
    {
        yield return [new SaveAlertRule("", null, SmsStatus.Failed, 1, true)];
        yield return [new SaveAlertRule(new string('x', 121), null, SmsStatus.Failed, 1, true)];
        yield return [new SaveAlertRule("Rule", null, (SmsStatus)99, 1, true)];
        yield return [new SaveAlertRule("Rule", null, SmsStatus.Failed, 0, true)];
        yield return [new SaveAlertRule("Rule", null, SmsStatus.Failed, 1441, true)];
        yield return [new SaveAlertRule("Rule", new string('p', 51), SmsStatus.Failed, 1, true)];
    }

    [Fact]
    public async Task UpdateDeleteAndMarkRead_ThrowWhenRepositoryDoesNotFindItem()
    {
        var service = new AlertService(new Repository(), new AlertRuleFactory(TimeProvider.System));
        var request = new SaveAlertRule("Rule", null, SmsStatus.Failed, 1, true);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateRuleAsync(tenantId, Guid.NewGuid(), request));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteRuleAsync(tenantId, Guid.NewGuid()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.MarkReadAsync(tenantId, Guid.NewGuid()));
    }

    [Theory]
    [InlineData(-1, 20)]
    [InlineData(0, 0)]
    [InlineData(0, 201)]
    public async Task ListAlerts_RejectsInvalidPagination(int skip, int take)
    {
        var service = new AlertService(new Repository(), new AlertRuleFactory(TimeProvider.System));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAlertsAsync(tenantId, false, skip, take));
    }

    [Fact]
    public async Task RepositoryOperations_AreDelegated()
    {
        var repository = new Repository { Found = true };
        var service = new AlertService(repository, new AlertRuleFactory(TimeProvider.System));
        Assert.Empty(await service.ListRulesAsync(tenantId));
        Assert.Empty(await service.ListAlertsAsync(tenantId, true, 0, 20));
        await service.DeleteRuleAsync(tenantId, Guid.NewGuid());
        await service.MarkReadAsync(tenantId, Guid.NewGuid());
        await service.MarkAllReadAsync(tenantId);
        Assert.True(repository.MarkAllReadCalled);
    }

    private sealed class Repository : IAlertRepository
    {
        public AlertRule? Rule { get; private set; }
        public bool Found { get; set; }
        public bool MarkAllReadCalled { get; private set; }
        public Task<IReadOnlyList<AlertRule>> ListRulesAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AlertRule>>([]);
        public Task CreateRuleAsync(AlertRule rule, CancellationToken cancellationToken = default) { Rule = rule; return Task.CompletedTask; }
        public Task<bool> UpdateRuleAsync(AlertRule rule, CancellationToken cancellationToken = default) { Rule = rule; return Task.FromResult(Found); }
        public Task<bool> DeleteRuleAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Found);
        public Task<IReadOnlyList<AlertNotification>> ListAlertsAsync(Guid tenantId, bool unreadOnly, int skip, int take, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AlertNotification>>([]);
        public Task<bool> MarkReadAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Found);
        public Task MarkAllReadAsync(Guid tenantId, CancellationToken cancellationToken = default) { MarkAllReadCalled = true; return Task.CompletedTask; }
        public Task<AlertRule?> GetRuleAsync(Guid tenantId, Guid ruleId, CancellationToken cancellationToken = default) => Task.FromResult<AlertRule?>(Rule);
        public Task EvaluateRuleAsync(Guid eventId, Guid ruleId, Guid tenantId, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
