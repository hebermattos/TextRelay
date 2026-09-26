using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using Sms.Api.Middleware;
using Sms.Infrastructure.Messaging;

namespace Sms.Infrastructure.Tests;

public sealed class InfrastructureBehaviorTests
{
    [Fact]
    public async Task RabbitMonitoringQueriesAllConfiguredQueues()
    {
        var requests = new List<string>();
        var handler = new Handler((request, _) =>
        {
            requests.Add(request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"messages_ready":0,"messages_unacknowledged":0,"consumers":1}""")
            };
        });
        var factory = Factory(new HttpClient(handler) { BaseAddress = new Uri("http://rabbit/") });
        var service = new RabbitMqMonitoringService(factory,
            new RabbitMqAlertOptions { VirtualHost = "/", Queue = "alerts", SendQueue = "send", ReportingQueue = "reports" },
            Mock.Of<ILogger<RabbitMqMonitoringService>>());

        await service.CollectAsync(default);

        Assert.Contains(requests, x => x.EndsWith("api/queues/%2F/alerts"));
        Assert.Contains(requests, x => x.EndsWith("api/queues/%2F/send"));
        Assert.Contains(requests, x => x.EndsWith("api/queues/%2F/reports"));
    }

    [Fact]
    public async Task RabbitMonitoringLogsCollectionFailureWithoutCrashing()
    {
        var logger = new Mock<ILogger<RabbitMqMonitoringService>>();
        var handler = new Handler((_, _) => throw new HttpRequestException("offline"));
        var service = new RabbitMqMonitoringService(
            Factory(new HttpClient(handler) { BaseAddress = new Uri("http://rabbit/") }),
            new RabbitMqAlertOptions { VirtualHost = "/", Queue = "alerts", SendQueue = "send", ReportingQueue = "reports" },
            logger.Object);

        await service.CollectAsync(default);

        logger.Verify(x => x.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((_, _) => true),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task UserActivityWriterFailsOpenWhenDatabaseIsUnavailable()
    {
        var logger = new Mock<ILogger<PostgresUserActivityWriter>>();
        var writer = new PostgresUserActivityWriter(
            "Host=127.0.0.1;Port=1;Database=missing;Username=x;Password=x;Timeout=1",
            TimeProvider.System,
            logger.Object);

        await writer.WriteAsync(new UserActivity(Guid.NewGuid(), "user", "Action", "Send", "Message", "1", "Sent.", "Succeeded"));

        logger.Verify(x => x.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((_, _) => true),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    private static IHttpClientFactory Factory(HttpClient client)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient("RabbitMqManagement")).Returns(client);
        return factory.Object;
    }


    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request, cancellationToken));
    }
}
