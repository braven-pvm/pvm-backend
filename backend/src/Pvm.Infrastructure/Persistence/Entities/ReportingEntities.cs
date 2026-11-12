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

    /// <summary>The financial year of the document date's calendar month.</summary>
    public int FinancialYear { get; set; }

    /// <summary>The month of the financial year, from 1 (March) to 12 (February).</summary>
    public int FinancialMonth { get; set; }

    /// <summary>The Acumatica posting period, such as 202601. Kept for checks; reports do not group by it.</summary>
    public string? PostPeriod { get; set; }

    public required string CustomerId { get; set; }
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
