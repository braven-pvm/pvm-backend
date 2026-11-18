namespace Pvm.Infrastructure.Persistence.Entities;

// The sell-out part of the reporting schema: what stores and wholesalers sold to their own
// customers, from the files of the retailers. Refer to docs/design/sell-out-import.md.

public static class SellOutImportStatuses
{
    /// <summary>The file is read and shown to a person. Nothing is loaded.</summary>
    public const string Previewed = "Previewed";

    /// <summary>The rows of the file are in the facts.</summary>
    public const string Loaded = "Loaded";

    /// <summary>The import did not load the file: an unknown format, too many bad rows, or a file loaded before.</summary>
    public const string Refused = "Refused";

    /// <summary>The load stopped with a defect.</summary>
    public const string Failed = "Failed";
}

public static class SellOutMapMethods
{
    public const string Gtin = "gtin";
    public const string ShopriteItemMapping = "shoprite-item-mapping";
    public const string Manual = "manual";
}

/// <summary>One received file.</summary>
public sealed class SellOutImportEntity
{
    public Guid Id { get; set; }

    /// <summary>The file format, such as makro-dispo, shoprite-vas or wholesaler-cj.</summary>
    public required string Source { get; set; }

    public required string FileName { get; set; }

    /// <summary>The SHA-256 hash of the file, as hex. A file loads only once.</summary>
    public required string FileSha256 { get; set; }

    public long FileSize { get; set; }

    /// <summary>The date in the file, or the date it came in if the file has none.</summary>
    public DateOnly FileDate { get; set; }

    /// <summary>upload:&lt;user&gt; or mail:&lt;sender&gt;.</summary>
    public required string ReceivedBy { get; set; }

    public required string Status { get; set; }

    /// <summary>The first day of the first month in the file.</summary>
    public DateOnly? FirstMonth { get; set; }

    /// <summary>The first day of the last month in the file.</summary>
    public DateOnly? LastMonth { get; set; }

    public int RowsRead { get; set; }
    public int RowsLoaded { get; set; }
    public int RowsUnmapped { get; set; }
    public int RowsRefused { get; set; }

    /// <summary>Why the import refused or failed the file.</summary>
    public string? StatusReason { get; set; }

    /// <summary>The raw file in the payload archive.</summary>
    public string? ArchivePath { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LoadedAt { get; set; }
}

/// <summary>One row of a file that the import could not use.</summary>
public sealed class SellOutImportIssueEntity
{
    public Guid Id { get; set; }
    public Guid ImportId { get; set; }

    /// <summary>The row number in the file, from 1.</summary>
    public int RowNumber { get; set; }

    public required string Reason { get; set; }

    /// <summary>The store and product values of the row, so that a person can find it.</summary>
    public string? RawKey { get; set; }
}

/// <summary>
/// A retailer store, or a customer of a wholesaler. The import makes the row the first time a
/// store code comes in. A person can add the missing details later.
/// </summary>
public sealed class RetailStoreEntity
{
    public Guid Id { get; set; }

    /// <summary>makro, shoprite-group, or the wholesaler.</summary>
    public required string Retailer { get; set; }

    /// <summary>The code of the retailer, such as the 4-digit SAP store number.</summary>
    public required string StoreCode { get; set; }

    public required string Name { get; set; }
    public string? Brand { get; set; }
    public string? Region { get; set; }
    public string? Province { get; set; }
    public string? Gln { get; set; }

    /// <summary>The Acumatica customer, where PVM invoices the store directly.</summary>
    public string? AcumaticaCustomerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Units that one store sold of one product in one calendar month, from one source.</summary>
public sealed class SellOutMonthEntity
{
    public Guid Id { get; set; }
    public Guid ImportId { get; set; }
    public required string Source { get; set; }
    public Guid StoreId { get; set; }

    /// <summary>The Acumatica item. Null until the product code is mapped.</summary>
    public string? InventoryId { get; set; }

    /// <summary>The product code in the file: a GTIN, a retailer article number or a wholesaler code.</summary>
    public required string SourceProductCode { get; set; }

    /// <summary>The barcode that the file gives for the product, if any. A later GTIN load maps the row by it.</summary>
    public string? Gtin { get; set; }

    public int Year { get; set; }
    public int Month { get; set; }
    public int FinancialYear { get; set; }
    public int FinancialMonth { get; set; }

    /// <summary>Units sold, in the unit of the source.</summary>
    public decimal Units { get; set; }

    /// <summary>Units in PVM selling units, after the conversion of the product map.</summary>
    public decimal UnitsEach { get; set; }

    /// <summary>The value without VAT, where the source gives it.</summary>
    public decimal? ValueExclVat { get; set; }

    /// <summary>True if the month was not complete on the file date.</summary>
    public bool IsPartial { get; set; }
}

/// <summary>The stock of one product in one store on the file date.</summary>
public sealed class StoreStockSnapshotEntity
{
    public Guid Id { get; set; }
    public Guid ImportId { get; set; }
    public required string Source { get; set; }
    public Guid StoreId { get; set; }
    public string? InventoryId { get; set; }
    public required string SourceProductCode { get; set; }
    public string? Gtin { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public decimal? StockOnHand { get; set; }
    public decimal? StockOnOrder { get; set; }
    public decimal? StockInTransit { get; set; }
    public decimal? StockValue { get; set; }
    public decimal? WeeksOfStock { get; set; }
    public decimal? DcStock { get; set; }
}

/// <summary>A product code of one source, mapped to an Acumatica item.</summary>
public sealed class SellOutProductMapEntity
{
    public required string Source { get; set; }
    public required string SourceProductCode { get; set; }
    public required string InventoryId { get; set; }

    /// <summary>PVM selling units in one unit of the source. Usually 1.</summary>
    public decimal UnitsPerEach { get; set; } = 1m;

    public required string Method { get; set; }
    public required string MappedBy { get; set; }
    public DateTimeOffset MappedAt { get; set; }
}

/// <summary>A GTIN (barcode) of an Acumatica item, from the cross references of the stock item.</summary>
public sealed class ProductGtinEntity
{
    public required string Gtin { get; set; }
    public required string InventoryId { get; set; }
    public string? Uom { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
