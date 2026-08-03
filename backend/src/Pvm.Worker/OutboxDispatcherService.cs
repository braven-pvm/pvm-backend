using Microsoft.Extensions.Options;
using Pvm.Infrastructure.Messaging;

namespace Pvm.Worker;

public sealed class OutboxDispatcherService(
    IServiceScopeFactory scopeFactory,
    IIntegrationMessagePublisher publisher,
    IOptions<ServiceBusOptions> options,
    ILogger<OutboxDispatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.OutboxPollSeconds));

        do
        {
            try
            {
                await DispatchBatchAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox dispatch cycle failed; the next poll will retry.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DispatchBatchAsync(ServiceBusOptions settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<OutboxRepository>();
        var lease = await repository.ClaimAsync(
            settings.OutboxBatchSize,
            TimeSpan.FromSeconds(settings.OutboxLeaseSeconds),
            cancellationToken);

        foreach (var message in lease.Messages)
        {
            try
            {
                await publisher.PublishAsync(message, cancellationToken);
                await repository.MarkPublishedAsync(message.Id, lease.LeaseId, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception,
                    "Outbox publish failed for message {MessageId} on {QueueName}.",
                    message.Id,
                    message.QueueName);
                await repository.ReleaseAsync(
                    message.Id,
                    lease.LeaseId,
                    "broker-publish-failed",
                    exception.Message,
                    TimeSpan.FromSeconds(Math.Min(300, 5 * message.PublishAttempts)),
                    cancellationToken);
            }
        }
    }
}
