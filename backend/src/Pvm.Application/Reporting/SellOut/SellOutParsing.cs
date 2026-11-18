namespace Pvm.Application.Reporting.SellOut;

/// <summary>A received sell-out file.</summary>
/// <param name="ReceivedOn">The date the file came in. A file with no date of its own uses it.</param>
public sealed record SellOutFile(string FileName, byte[] Content, DateOnly ReceivedOn);

/// <summary>
/// The units that one store sold of one product in one calendar month.
/// </summary>
/// <param name="UnitsEach">Units in single items (each).</param>
/// <param name="ValueExclVat">The value without VAT, where the source gives it.</param>
/// <param name="IsPartial">True if the month was not complete on the file date.</param>
public sealed record SellOutMonthRow(
    string StoreCode,
    string StoreName,
    string ProductCode,
    string? Gtin,
    int Year,
    int Month,
    decimal UnitsEach,
    decimal? ValueExclVat,
    bool IsPartial);

/// <summary>The stock of one product in one store on the file date, in single items.</summary>
public sealed record SellOutStockRow(
    string StoreCode,
    string StoreName,
    string ProductCode,
    string? Gtin,
    decimal? StockOnHand,
    decimal? StockOnOrder,
    decimal? StockInTransit,
    decimal? StockValue,
    decimal? WeeksOfStock,
    decimal? DcStock);

/// <summary>A row that the parser could not use.</summary>
public sealed record SellOutParseIssue(int RowNumber, string Reason, string? RawKey);

/// <summary>What a parser read from one file.</summary>
/// <param name="Source">The file format, such as makro-dispo.</param>
/// <param name="Retailer">The retailer of the stores, such as makro.</param>
/// <param name="FileDate">The date in the file, or the date it came in.</param>
/// <param name="Months">The first day of each month that the file covers. The import replaces these months.</param>
/// <param name="RowsRead">The data rows in the file.</param>
/// <param name="RowsIgnored">Rows that hold no sales on purpose, such as summary rows.</param>
public sealed record SellOutParseResult(
    string Source,
    string Retailer,
    DateOnly FileDate,
    IReadOnlyList<DateOnly> Months,
    IReadOnlyList<SellOutMonthRow> MonthRows,
    IReadOnlyList<SellOutStockRow> StockRows,
    IReadOnlyList<SellOutParseIssue> Issues,
    int RowsRead,
    int RowsIgnored);

/// <summary>Reads one sell-out file format.</summary>
public interface ISellOutParser
{
    /// <summary>The file format, such as makro-dispo.</summary>
    string Source { get; }

    /// <summary>True if the file has the header of this format. The check reads only the start of the file.</summary>
    bool CanRead(SellOutFile file);

    /// <summary>Reads the file. Throws <see cref="SellOutFormatException"/> if the file cannot be read at all.</summary>
    SellOutParseResult Parse(SellOutFile file);
}

/// <summary>The file does not have the shape of its format. The import refuses it.</summary>
public sealed class SellOutFormatException(string message) : Exception(message);
