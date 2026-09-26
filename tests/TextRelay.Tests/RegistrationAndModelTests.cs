using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sms.Application;
using Sms.Application.Alerts;
using Sms.Application.Messages;
using Sms.Application.Providers;
using Sms.Application.Tenants;
using Sms.Domain.Tenants;
using Sms.Domain.Messages;
using Sms.Infrastructure;
using Sms.Infrastructure.Providers;
using Sms.Infrastructure.Caching;
using Sms.Infrastructure.Persistence;
using Sms.Infrastructure.Observability;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sms.Infrastructure.Tests;

public sealed class RegistrationAndModelTests
{
    [Fact]
    public void AddApplication_RegistersApplicationServices()
    {
        var services = new ServiceCollection();

        services.AddApplication();

        Assert.Contains(services, x => x.ServiceType == typeof(SendSmsService) && x.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, x => x.ServiceType == typeof(ReceiveSmsWebhookService) && x.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, x => x.ServiceType == typeof(TenantProvisioningService) && x.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, x => x.ServiceType == typeof(AlertService) && x.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, x => x.ServiceType == typeof(SendSmsValidator) && x.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, x => x.ServiceType == typeof(AlertRuleFactory) && x.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddInfrastructure_RegistersProvidersAndRepositories()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=sms_api;Username=sms;Password=Password1!",
            ["ConnectionStrings:ReportingPostgres"] = "Host=localhost;Database=sms_api_reporting;Username=sms;Password=Password1!",
            ["ConnectionStrings:Redis"] = "localhost:6379",
            ["Encryption:MasterKey"] = Convert.ToBase64String(new byte[32])
        }).Build();
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);

        Assert.Equal(3, services.Count(x => x.ServiceType == typeof(ISmsProvider)));
        Assert.Contains(services, x => x.ServiceType == typeof(ISmsProvider) && x.ImplementationType == typeof(MockSmsProvider));
        Assert.Contains(services, x => x.ServiceType == typeof(BandwidthSmsProvider));
        Assert.Contains(services, x => x.ServiceType == typeof(BandwidthWebhookParser) && x.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, x => x.ServiceType == typeof(IHttpClientFactory));
        Assert.Contains(services, x => x.ServiceType == typeof(IDistributedCache));
        Assert.Contains(services, x => x.ServiceType == typeof(TenantConfigurationCache) && x.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, x => x.ServiceType == typeof(ISmsProviderResolver));
        Assert.Contains(services, x => x.ServiceType == typeof(ISmsMessageRepository));
        Assert.Contains(services, x => x.ServiceType == typeof(IAlertRepository));
        Assert.Contains(services, x => x.ServiceType == typeof(ITenantSmsProviderRepository));
        Assert.Contains(services, x => x.ServiceType == typeof(ISmsWebhookUrlProvider));
        Assert.Contains(services, x => x.ServiceType == typeof(Sms.Infrastructure.Persistence.ReportingSqlConnectionFactory));
        Assert.Contains(services, x => x.ServiceType == typeof(Sms.Infrastructure.Messaging.ITenantSmsOverviewOutbox) && x.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, x => x.ServiceType == typeof(Sms.Infrastructure.Messaging.ITenantSmsOverviewEventPublisher) && x.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddInfrastructure_DoesNotRequireRedisConnectionWhenCacheIsDisabled()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Enabled"] = "false",
            ["Encryption:MasterKey"] = Convert.ToBase64String(new byte[32])
        }).Build();
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<DisabledDistributedCacheProxy>(provider.GetRequiredService<IDistributedCache>());
    }

    [Fact]
    public void CacheConfiguration_DefaultsToEnabled()
    {
        Assert.True(CacheConfiguration.IsEnabled(new ConfigurationBuilder().Build()));
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("invalid", true)]
    public void CacheConfiguration_ParsesConfiguration(string value, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Cache:Enabled"] = value })
            .Build();

        Assert.Equal(expected, CacheConfiguration.IsEnabled(configuration));
    }

    [Fact]
    public async Task MockProvider_ReturnsFlowTestResult()
    {
        var provider = new MockSmsProvider();
        var result = await provider.SendAsync("+15550000000", "+15550000001", "test");

        Assert.StartsWith("mock-", result.ProviderMessageId);
        Assert.Contains(result.Status, new[] { SmsStatus.Pending, SmsStatus.Sent, SmsStatus.Delivered, SmsStatus.Failed });
    }

    [Fact]
    public void TextRelayTelemetry_ExposesConfiguredSourcesAndInstruments()
    {
        Assert.Equal("TextRelay", TextRelayTelemetry.ActivitySourceName);
        Assert.Equal("TextRelay", TextRelayTelemetry.MeterName);
        Assert.Equal(TextRelayTelemetry.ActivitySourceName, TextRelayTelemetry.ActivitySource.Name);
        Assert.Equal(TextRelayTelemetry.MeterName, TextRelayTelemetry.Meter.Name);
        Assert.NotNull(TextRelayTelemetry.SmsQueued);
        Assert.NotNull(TextRelayTelemetry.SmsSent);
        Assert.NotNull(TextRelayTelemetry.SmsFailed);
        Assert.NotNull(TextRelayTelemetry.SmsClaimRejected);
        Assert.NotNull(TextRelayTelemetry.QueuePublishFailed);
        Assert.NotNull(TextRelayTelemetry.ProviderDuration);
        Assert.NotNull(TextRelayTelemetry.ProcessingDuration);
    }

    [Fact]
    public void ActivityExtensions_RecordExceptionAddsSafeExceptionEvent()
    {
        using var activity = new Activity("test").Start();

        activity.RecordException(new InvalidOperationException("failed"));

        var exceptionEvent = Assert.Single(activity.Events);
        Assert.Equal("exception", exceptionEvent.Name);
        Assert.Contains(exceptionEvent.Tags, tag => tag.Key == "exception.type" && Equals(tag.Value, typeof(InvalidOperationException).FullName));
        Assert.Contains(exceptionEvent.Tags, tag => tag.Key == "exception.message" && Equals(tag.Value, "failed"));
        Assert.DoesNotContain(exceptionEvent.Tags, tag => tag.Key == "exception.stacktrace");
    }

    [Fact]
    public void TextRelayTelemetry_ActivitySourceCreatesActivityWhenObserved()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TextRelayTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = TextRelayTelemetry.ActivitySource.StartActivity("test");

        Assert.NotNull(activity);
        Assert.Equal("test", activity.OperationName);
    }

    [Fact]
    public void TextRelayTelemetry_MeterPublishesDomainMeasurements()
    {
        var measurements = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == TextRelayTelemetry.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => measurements.Add(instrument.Name));
        listener.SetMeasurementEventCallback<double>((instrument, _, _, _) => measurements.Add(instrument.Name));
        listener.Start();

        TextRelayTelemetry.SmsQueued.Add(1);
        TextRelayTelemetry.SmsSent.Add(1);
        TextRelayTelemetry.SmsFailed.Add(1);
        TextRelayTelemetry.SmsClaimRejected.Add(1);
        TextRelayTelemetry.QueuePublishFailed.Add(1);
        TextRelayTelemetry.ProviderDuration.Record(1);
        TextRelayTelemetry.ProcessingDuration.Record(1);

        Assert.Contains("sms.queued", measurements);
        Assert.Contains("sms.sent", measurements);
        Assert.Contains("sms.failed", measurements);
        Assert.Contains("sms.queue.claim.rejected", measurements);
        Assert.Contains("sms.queue.publish.failed", measurements);
        Assert.Contains("sms.provider.duration", measurements);
        Assert.Contains("sms.processing.duration", measurements);
    }

    [Fact]
    public void TenantAndProviderConfiguration_ExposeConfiguredValues()
    {
        var tenantId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var tenant = new Tenant { Id = tenantId, Name = "Tenant", CreatedAt = createdAt };
        var provider = new TenantSmsProviderConfiguration(tenantId, "Twilio", "account", "secret", "+1", true, true);

        Assert.Equal(tenantId, tenant.Id);
        Assert.Equal("Tenant", tenant.Name);
        Assert.True(tenant.IsActive);
        Assert.Equal(createdAt, tenant.CreatedAt);
        Assert.Equal(tenantId, provider.TenantId);
        Assert.Null(provider.Settings);
    }
}
