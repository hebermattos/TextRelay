using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sms.Infrastructure;
using Sms.Infrastructure.Messaging;

namespace Sms.Infrastructure.Tests;

public sealed class InfrastructureWorkerRegistrationTests
{
    [Fact]
    public void AddInfrastructureWorkers_RegistersExpectedBackgroundServices()
    {
        var services = new ServiceCollection();

        services.AddInfrastructureWorkers();

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
