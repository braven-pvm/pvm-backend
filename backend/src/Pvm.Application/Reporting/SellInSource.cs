using Pvm.Domain.Reporting;

namespace Pvm.Application.Reporting;

/// <summary>
/// One sales line from the Acumatica inquiry PVM-Sales-Lines, as Acumatica gives it.
/// Quantity and amount are not signed yet: Acumatica keeps credit memo lines positive.
/// </summary>
public sealed record SellInRow(
    SellInDocumentType DocumentType,
    string ReferenceNumber,
    int LineNumber,
    DateOnly DocumentDate,
    string? PostPeriod,
    string CustomerId,
    string CustomerName,
    string? CustomerClassId,
    string? CustomerClassName,
    string? Province,
    string? SalesRepId,
    string? SalesRepName,
    string InventoryId,
    string ItemDescription,
    string? ItemClassId,
    string? ItemClassName,
    string? WarehouseId,
    string? WarehouseName,
    string? LocationId,
    string? LocationName,
    decimal Quantity,
    string? Uom,
    decimal BaseQuantity,
    decimal Amount,
    DateTimeOffset? LastModifiedAt);

/// <summary>What one read of one posting period returned.</summary>
/// <param name="RawJson">The response body as received, for the raw store.</param>
/// <param name="Rows">The rows that parsed.</param>
/// <param name="SkippedRows">Rows that were not sales lines, such as a payment line.</param>
public sealed record SellInPeriodRead(
    PostingPeriod Period,
    string RawJson,
    IReadOnlyList<SellInRow> Rows,
    int SkippedRows);

/// <summary>Reads Acumatica sell-in, one posting period for each call.</summary>
public interface ISellInSource
{
    Task<SellInPeriodRead> ReadPeriodAsync(PostingPeriod period, CancellationToken cancellationToken);
}
