using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Pvm.Application.Messaging;
using Pvm.Application.Submissions;
using Pvm.Infrastructure.Acumatica;
using Pvm.Infrastructure.Messaging;
using Pvm.Infrastructure.Operations;
using Pvm.Infrastructure.PayloadArchive;
using Pvm.Infrastructure.Investec;
using Pvm.Infrastructure.Persistence;
using Pvm.Infrastructure.Shoprite;
using Pvm.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPvmPersistence(builder.Configuration);

var enqueueShopriteRefresh = args.Contains("--enqueue-shoprite-po-refresh", StringComparer.OrdinalIgnoreCase);
var enqueueAcumaticaReconciliation = args.Contains(
    "--enqueue-acumatica-invoice-reconciliation",
    StringComparer.OrdinalIgnoreCase);
var enqueueAcumaticaLookback = args.Contains(
    "--enqueue-acumatica-invoice-lookback",
    StringComparer.OrdinalIgnoreCase);
var refreshInvestecFeed = args.Contains(
    "--refresh-investec-bank-feed",
    StringComparer.OrdinalIgnoreCase);

if (enqueueShopriteRefresh || enqueueAcumaticaReconciliation || enqueueAcumaticaLookback)
{
    using var schedulerHost = builder.Build();
    await using var scope = schedulerHost.Services.CreateAsyncScope();
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

    if (enqueueShopriteRefresh)
    {
        var runQueue = scope.ServiceProvider.GetRequiredService<ShopritePurchaseOrderRefreshRunQueue>();
        var intervalMinutes = Math.Max(
            1,
            builder.Configuration.GetValue<int?>(
                $"{ShopritePurchaseOrderRefreshOptions.SectionName}:ScheduleIntervalMinutes") ?? 5);
        var window = ScheduleWindow(DateTimeOffset.UtcNow, intervalMinutes);
        var scheduleKey = $"shoprite-po-refresh:{window:yyyyMMddHHmm}";
        var queued = await runQueue.EnqueueAsync(
            IntegrationRunTriggers.Scheduled,
            "system:scheduler",
            scheduleKey,
            CancellationToken.None);
        LogQueued(
            loggerFactory.CreateLogger("ShopritePurchaseOrderRefreshScheduler"),
            IntegrationRunTypes.ShopritePurchaseOrderRefresh,
            queued,
            scheduleKey);
    }
    else
    {
        var runQueue = scope.ServiceProvider.GetRequiredService<AcumaticaInvoiceReconciliationRunQueue>();
        var intervalMinutes = Math.Max(
            1,
            builder.Configuration.GetValue<int?>(
                $"{AcumaticaReconciliationOptions.SectionName}:ScheduleIntervalMinutes") ?? 10);
        var lookbackDays = Math.Max(
            1,
            builder.Configuration.GetValue<int?>(
                $"{AcumaticaReconciliationOptions.SectionName}:DailyLookbackDays") ?? 7);
        var window = enqueueAcumaticaLookback
            ? new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero)
            : ScheduleWindow(DateTimeOffset.UtcNow, intervalMinutes);
        var trigger = enqueueAcumaticaLookback
            ? IntegrationRunTriggers.DailyLookback
            : IntegrationRunTriggers.Scheduled;
        var scheduleKey = enqueueAcumaticaLookback
            ? $"acumatica-invoice-lookback:{window:yyyyMMdd}"
            : $"acumatica-invoice-reconciliation:{window:yyyyMMddHHmm}";
        var queued = await runQueue.EnqueueAsync(
            trigger,
            "system:scheduler",
            scheduleKey,
            window,
            enqueueAcumaticaLookback ? lookbackDays : null,
            CancellationToken.None);
        LogQueued(
            loggerFactory.CreateLogger("AcumaticaInvoiceReconciliationScheduler"),
            IntegrationRunTypes.AcumaticaInvoiceReconciliation,
            queued,
            scheduleKey);
    }

    return;
}

if (refreshInvestecFeed)
{
    // The Investec pull runs inline. It happens once a day, so it needs no queue and no consumer.
    builder.Services.AddAcumaticaInvoiceSource(builder.Configuration);
    builder.Services.AddInvestecBankFeed(builder.Configuration);

    using var feedHost = builder.Build();
    await using var feedScope = feedHost.Services.CreateAsyncScope();
    var feedLogger = feedScope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("InvestecBankFeedScheduler");
    var refreshService = feedScope.ServiceProvider.GetRequiredService<InvestecBankFeedRefreshService>();
    var investecOptions = feedScope.ServiceProvider.GetRequiredService<IOptions<InvestecOptions>>().Value;

    // The window overlaps earlier runs on purpose, so a missed run is collected by the next one.
    // It never reaches before the date the feed took ownership of the account.
    var (fromDate, toDate) = InvestecRefreshWindow.Resolve(
        DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
        investecOptions.RefreshLookbackDays,
        investecOptions.FeedStartDate);

    try
    {
        var result = await refreshService.RefreshAsync(fromDate, toDate, CancellationToken.None);
        feedLogger.LogInformation(
            "integration.run.completed RunType=investec-bank-feed From={From} To={To} Retrieved={Retrieved} Imported={Imported} Statement={Statement}",
            fromDate,
            toDate,
            result.TransactionsRetrieved,
            result.LinesImported,
            result.StatementReference ?? "(none)");
    }
    catch (Exception exception)
    {
        // Rethrow so that the Container Apps job reports a failed replica.
        feedLogger.LogError(
            exception,
            "integration.run.failed RunType=investec-bank-feed From={From} To={To}",
            fromDate,
            toDate);
        throw;
    }

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

static DateTimeOffset ScheduleWindow(DateTimeOffset now, int intervalMinutes)
{
    var intervalTicks = TimeSpan.FromMinutes(intervalMinutes).Ticks;
    return new DateTimeOffset(now.UtcTicks - (now.UtcTicks % intervalTicks), TimeSpan.Zero);
}

static void LogQueued(
    ILogger logger,
    string runType,
    QueuedIntegrationRun queued,
    string scheduleKey)
    => logger.LogInformation(
        "integration.run.queued RunType={RunType} RunId={RunId} MessageId={MessageId} Created={Created} ScheduleKey={ScheduleKey}",
        runType,
        queued.RunId,
        queued.MessageId,
        queued.Created,
        scheduleKey);
