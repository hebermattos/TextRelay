using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sms.Infrastructure.Messaging;

public abstract class PollingBackgroundService(
    TimeSpan pollingInterval,
    ILogger logger) : BackgroundService
{
    protected abstract string FailureMessage { get; }
    protected abstract Task ExecuteIterationAsync(CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteIterationAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, FailureMessage);
            }

            await Task.Delay(pollingInterval, stoppingToken);
        }
    }
}
