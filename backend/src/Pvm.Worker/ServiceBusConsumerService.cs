using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Pvm.Application.Messaging;
using Pvm.Infrastructure.Messaging;

namespace Pvm.Worker;

public sealed class ServiceBusConsumerService(
    ServiceBusClient client,
    IServiceScopeFactory scopeFactory,
    IOptions<ServiceBusOptions> options,
    ILogger<ServiceBusConsumerService> logger) : IHostedService, IAsyncDisposable
{
    private readonly List<ServiceBusProcessor> _processors = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var queueName in new[]
        {
            IntegrationQueues.ShopritePurchaseOrderRefresh,
            IntegrationQueues.AcumaticaInvoiceDiscovery,
            IntegrationQueues.ShopriteInvoiceSubmit
        })
        {
            var processor = client.CreateProcessor(queueName, new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = 1,
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10)
            });
            processor.ProcessMessageAsync += args => ProcessAsync(queueName, args);
            processor.ProcessErrorAsync += ProcessErrorAsync;
            _processors.Add(processor);
            await processor.StartProcessingAsync(cancellationToken);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var processor in _processors)
        {
            await processor.StopProcessingAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var processor in _processors)
        {
            await processor.DisposeAsync();
        }
    }

    private async Task ProcessAsync(string queueName, ProcessMessageEventArgs args)
    {
        using var scope = scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IntegrationMessageProcessor>();
        var result = await processor.ProcessAsync(
            queueName,
            args.Message.MessageId,
            args.Message.Body.ToString(),
            args.Message.DeliveryCount,
            options.Value.MaxDeliveryCount,
            args.Message.EnqueuedTime,
            args.CancellationToken);

        switch (result.Settlement)
        {
            case MessageSettlement.Complete:
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                break;
            case MessageSettlement.Abandon:
                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
                break;
            case MessageSettlement.DeadLetter:
                await args.DeadLetterMessageAsync(
                    args.Message,
                    result.DeadLetterReason ?? "handler-rejected",
                    OutboxRepository.SafeSummary(result.ErrorSummary) ?? "Message processing failed.",
                    args.CancellationToken);
                break;
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception,
            "Service Bus processing error for {EntityPath} from {ErrorSource}.",
            args.EntityPath,
            args.ErrorSource);
        return Task.CompletedTask;
    }
}
