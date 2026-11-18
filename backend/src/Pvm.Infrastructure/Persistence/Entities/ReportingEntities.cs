using Pvm.Domain.Reporting;

namespace Pvm.Infrastructure.Persistence.Entities;

// The reporting schema. Dimensions are keyed by their Acumatica ID, so a load can upsert them
// without a lookup. Facts carry the IDs, not foreign keys: a sales line must load even when a
// dimension row is late, and a report shows such a line as "Unknown" instead of dropping it.

/// <summary>An Acumatica customer, as reports group it.</summary>
public sealed class ReportingCustomerEntity
{
    public required string CustomerId { get; set; }
    public required string Name { get; set; }
    public string? CustomerClassId { get; set; }
    public string? CustomerClassName { get; set; }
    public string? Province { get; set; }

    /// <summary>
    /// The default salesperson on the customer now. Reports show all sales of the customer under
    /// this rep, as the sales workbook does. The rep at the sale stays on each line.
    /// </summary>
    public string? CurrentSalesRepId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>An Acumatica stock item, as reports group it.</summary>
public sealed class ReportingProductEntity
{
    public required string InventoryId { get; set; }
    public required string Description { get; set; }
    public string? ItemClassId { get; set; }
    public string? ItemClassName { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>An Acumatica salesperson.</summary>
public sealed class ReportingSalesRepEntity
{
    public required string SalesRepId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A warehouse location. In PVM's warehouses the location name says what kind of stock was sold,
/// such as finished goods or short-dated stock (the "type of good" in the sales workbook).
/// </summary>
public sealed class ReportingWarehouseLocationEntity
{
    public required string WarehouseId { get; set; }
    public required string LocationId { get; set; }
    public string? WarehouseName { get; set; }
    public string? LocationName { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// The sales channel of a customer class, such as Retail or Wholesale and distribution.
/// A class with no row is in the channel "Other". People maintain this table.
/// </summary>
public sealed class ReportingCustomerClassChannelEntity
{
    public required string CustomerClassId { get; set; }
    public required string Channel { get; set; }
    public required string UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// One line of a released Acumatica AR invoice, credit memo or debit memo: PVM's sell-in.
/// Quantity and amount are signed: a credit memo line is negative.
/// </summary>
public sealed class SellInLineEntity
{
    public Guid Id { get; set; }
    public SellInDocumentType DocumentType { get; set; }
    public required string ReferenceNumber { get; set; }
    public int LineNumber { get; set; }
    public DateOnly DocumentDate { get; set; }

    /// <summary>The financial year of the posting period.</summary>
    public int FinancialYear { get; set; }

    /// <summary>
    /// The month of the financial year, from 1 (March) to 12 (February): the number of the
    /// posting period. PVM closes a period near the 25th, so a document of the last days of a
    /// month counts in the next month, as in the sales workbook.
    /// </summary>
    public int FinancialMonth { get; set; }

    /// <summary>The Acumatica posting period, such as 202601.</summary>
    public string? PostPeriod { get; set; }

    public required string CustomerId { get; set; }
    /// <summary>The salesperson on the document line: the rep at the sale.</summary>
    public string? SalesRepId { get; set; }

    public required string InventoryId { get; set; }
    public string? WarehouseId { get; set; }
    public string? LocationId { get; set; }
    public decimal Quantity { get; set; }
    public string? Uom { get; set; }
    public decimal BaseQuantity { get; set; }

    /// <summary>The line amount in rand, without VAT.</summary>
    public decimal Amount { get; set; }

    public DateTimeOffset? AcumaticaLastModifiedAt { get; set; }
    public DateTimeOffset LoadedAt { get; set; }
}

/// <summary>
/// One line of the view reporting.sell_in_report_lines: the sell-in that the sales report counts.
/// The view applies the rules of the old sales workbook: invoices and credit memos only, items
/// with an item class only, and the customer's current rep. Lines with no rep stay in.
/// </summary>
public sealed class SellInReportLine
{
    public Guid Id { get; set; }
    public SellInDocumentType DocumentType { get; set; }
    public required string ReferenceNumber { get; set; }
    public int LineNumber { get; set; }
    public DateOnly DocumentDate { get; set; }
    public int FinancialYear { get; set; }
    public int FinancialMonth { get; set; }
    public string? PostPeriod { get; set; }
    public required string CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerClassId { get; set; }
    public string? CustomerClassName { get; set; }
    public string? Province { get; set; }

    /// <summary>The customer's current rep.</summary>
    public string? SalesRepId { get; set; }

    public string? SalesRepName { get; set; }

    /// <summary>The rep on the document line, at the time of the sale.</summary>
    public string? SalesRepAtSaleId { get; set; }

    public string? SalesRepAtSaleName { get; set; }
    public required string InventoryId { get; set; }
    public string? ItemDescription { get; set; }
    public string? ItemClassId { get; set; }
    public string? ItemClassName { get; set; }
    public string? WarehouseId { get; set; }
    public string? LocationId { get; set; }
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>
/// One line of the view reporting.sell_in_excluded_lines: a sell-in line that the sales
/// workbook rules leave out, with the reason, so that a check can show it apart.
/// </summary>
public sealed class SellInExcludedLine
{
    public Guid Id { get; set; }
    public required string Reason { get; set; }
    public SellInDocumentType DocumentType { get; set; }
    public required string ReferenceNumber { get; set; }
    public int LineNumber { get; set; }
    public int FinancialYear { get; set; }
    public int FinancialMonth { get; set; }
    public required string CustomerId { get; set; }
    public required string InventoryId { get; set; }
    public decimal Amount { get; set; }
}
