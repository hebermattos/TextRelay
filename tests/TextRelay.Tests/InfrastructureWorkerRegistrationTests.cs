using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sms.Api.Shared;
using Sms.Infrastructure.Messaging;

namespace Sms.Infrastructure.Tests;

public sealed class InfrastructureWorkerRegistrationTests
{
    [Fact]
    public void AddWorkerServices_RegistersExpectedBackgroundServices()
    {
        var services = new ServiceCollection();

        services.AddWorkerServices();

        var hostedServiceTypes = services
            .Where(service => service.ServiceType == typeof(IHostedService))
            .Select(service => service.ImplementationType)
            .ToArray();

        Assert.Contains(typeof(RabbitMqMonitoringService), hostedServiceTypes);
        Assert.Contains(typeof(AlertEvaluationOutboxPublisher), hostedServiceTypes);
        Assert.Contains(typeof(TenantSmsOverviewOutboxPublisher), hostedServiceTypes);
        Assert.Contains(typeof(SmsQueuePublisherWorker), hostedServiceTypes);
    }
}
