using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Pvm.Application.Messaging;
using Pvm.Application.Submissions;
using Pvm.Infrastructure.Acumatica;
using Pvm.Infrastructure.Messaging;
using Pvm.Infrastructure.Operations;
using Pvm.Infrastructure.PayloadArchive;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Shoprite;
using Pvm.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPvmPersistence(builder.Configuration);

if (args.Contains("--enqueue-shoprite-po-refresh", StringComparer.OrdinalIgnoreCase))
{
    using var schedulerHost = builder.Build();
    await using var scope = schedulerHost.Services.CreateAsyncScope();
    var runQueue = scope.ServiceProvider.GetRequiredService<ShopritePurchaseOrderRefreshRunQueue>();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("ShopritePurchaseOrderRefreshScheduler");
    var intervalMinutes = Math.Max(
        1,
        builder.Configuration.GetValue<int?>(
            $"{ShopritePurchaseOrderRefreshOptions.SectionName}:ScheduleIntervalMinutes") ?? 5);
    var now = DateTimeOffset.UtcNow;
    var intervalTicks = TimeSpan.FromMinutes(intervalMinutes).Ticks;
    var window = new DateTimeOffset(
        now.Ticks - (now.Ticks % intervalTicks),
        TimeSpan.Zero);
    var scheduleKey = $"shoprite-po-refresh:{window:yyyyMMddHHmm}";
    var queued = await runQueue.EnqueueAsync(
        IntegrationRunTriggers.Scheduled,
        "system:scheduler",
        scheduleKey,
        CancellationToken.None);
    logger.LogInformation(
        "integration.run.queued RunType={RunType} RunId={RunId} MessageId={MessageId} Created={Created} ScheduleKey={ScheduleKey}",
        IntegrationRunTypes.ShopritePurchaseOrderRefresh,
        queued.RunId,
        queued.MessageId,
        queued.Created,
        scheduleKey);
    return;
}

builder.Services.AddPayloadArchive(builder.Configuration);
builder.Services.AddAcumaticaInvoiceSource(builder.Configuration);
builder.Services.AddShopritePurchaseOrderClient(builder.Configuration);
builder.Services.AddScoped<SubmitShopriteInvoiceHandler>();
builder.Services.AddWorkerShopriteInvoiceClient(builder.Configuration);
builder.Services.AddScoped<IntegrationMessageProcessor>();

builder.Services.AddOptions<ServiceBusOptions>()
    .Bind(builder.Configuration.GetSection(ServiceBusOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.FullyQualifiedNamespace),
        "ServiceBus:FullyQualifiedNamespace is required.")
    .Validate(options => options.MaxDeliveryCount >= 1, "ServiceBus:MaxDeliveryCount must be positive.")
    .Validate(options => options.OutboxBatchSize >= 1, "ServiceBus:OutboxBatchSize must be positive.")
    .Validate(options => options.OutboxPollSeconds >= 1, "ServiceBus:OutboxPollSeconds must be positive.")
    .Validate(options => options.OutboxLeaseSeconds >= 10, "ServiceBus:OutboxLeaseSeconds must be at least 10.")
    .ValidateOnStart();
builder.Services.AddSingleton(provider =>
{
    var options = provider.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
    return new ServiceBusClient(options.FullyQualifiedNamespace, new DefaultAzureCredential());
});
builder.Services.AddSingleton<IIntegrationMessagePublisher, ServiceBusMessagePublisher>();
builder.Services.AddHostedService<OutboxDispatcherService>();
builder.Services.AddHostedService<ServiceBusConsumerService>();

await builder.Build().RunAsync();
