using System.Text.Json;

namespace Pvm.Application.Messaging;

public static class IntegrationQueues
{
    public const string ShopritePurchaseOrderRefresh = "shoprite-po-refresh";
    public const string AcumaticaInvoiceDiscovery = "acumatica-invoice-discovery";
    public const string ShopriteInvoiceSubmit = "shoprite-invoice-submit";
}

public static class IntegrationMessageTypes
{
    public const string ShopritePurchaseOrderRefreshV1 = "shoprite.po-refresh.v1";
    public const string AcumaticaInvoiceDiscoveryV1 = "acumatica.invoice-discovery.v1";
    public const string AcumaticaInvoiceReconciliationV1 = "acumatica.invoice-reconciliation.v1";
    public const string ShopriteInvoiceSubmitV1 = "shoprite.invoice-submit.v1";
}

public sealed record IntegrationMessageEnvelope(
    Guid MessageId,
    string MessageType,
    string CorrelationId,
    string? CausationId,
    DateTimeOffset CreatedAt,
    JsonElement Data);

public sealed record RefreshShopritePurchaseOrdersMessage(
    string RequestedBy,
    Guid? RunId = null,
    string Trigger = IntegrationRunTriggers.Manual);

public sealed record DiscoverAcumaticaInvoicesMessage(string RequestedBy);

public sealed record ReconcileAcumaticaInvoicesMessage(
    string RequestedBy,
    Guid RunId,
    string Trigger,
    DateTimeOffset QueryTo,
    int? LookbackDays = null);

public sealed record SubmitShopriteInvoiceMessage(
    Guid CandidateId,
    Guid CommandId,
    string RequestedBy,
    string Mode = "automatic");

public interface IIntegrationCommandQueue
{
    Task<Guid> EnqueueAsync<T>(
        string queueName,
        string messageType,
        T data,
        string correlationId,
        string? causationId = null,
        Guid? messageId = null,
        CancellationToken cancellationToken = default);
}

public static class IntegrationRunTypes
{
    public const string ShopritePurchaseOrderRefresh = "shoprite-po-refresh";
    public const string AcumaticaInvoiceReconciliation = "acumatica-invoice-reconciliation";
}

public static class IntegrationRunTriggers
{
    public const string Manual = "manual";
    public const string Scheduled = "scheduled";
    public const string DailyLookback = "daily-lookback";
}

public static class IntegrationRunStatuses
{
    public const string Accepted = "Accepted";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}
