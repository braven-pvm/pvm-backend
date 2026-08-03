namespace Pvm.Infrastructure.Messaging;

public sealed class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    public string FullyQualifiedNamespace { get; set; } = string.Empty;
    public int MaxDeliveryCount { get; set; } = 5;
    public int OutboxBatchSize { get; set; } = 20;
    public int OutboxPollSeconds { get; set; } = 5;
    public int OutboxLeaseSeconds { get; set; } = 60;
}
