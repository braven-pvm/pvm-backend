using Pvm.Domain.Reporting;
using Pvm.Infrastructure.Reporting;

namespace Pvm.Infrastructure.Tests.Reporting;

public sealed class SellInRowParserTests
{
    // Made-up values in the shape that the QA inquiry returns: decimals as strings, local times.
    internal static string Row(
        string type = "INV",
        string reference = "IN000001",
        int line = 1,
        string date = "2024-03-04T00:00:00",
        string customer = "C0001",
        string item = "ITEM01",
        string amount = "120.50",
        string quantity = "2",
        string? rep = "REP01",
        string location = "FG") => $$"""
        {
          "TranType": "{{type}}", "RefNbr": "{{reference}}", "LineNbr": {{line}},
          "CustomerID": "{{customer}}", "CustomerClassID": "RETAIL", "SalesRepID": {{(rep is null ? "null" : $"\"{rep}\"")}},
          "InventoryID": "{{item}}", "ItemClassID": "BARS", "WarehouseID": "HO", "LocationID": "{{location}}",
          "TranDate": "{{date}}", "TranPeriodID": "012025", "FinPeriodID": "012025",
          "CustomerName": "Example Store", "CustomerClassName": "Example Retail", "Province": "GP",
          "SalesRepName": "Example Rep", "ItemDescription": "Example Bar 20x45g", "ItemClassName": "Energy Bars",
          "WarehouseName": "Head Office", "LocationName": "Finished Goods",
          "Quantity": "{{quantity}}", "UOM": "BOX", "BaseQuantity": "40", "Amount": "{{amount}}",
          "LastModified": "2024-03-04T10:15:00", "AddressID": 1, "Warehouse": "HO"
        }
        """;

    internal static string Response(params string[] rows) => $$"""{ "odata.metadata": "x", "value": [ {{string.Join(",", rows)}} ] }""";

    [Fact]
    public void Sales_line_is_read_with_all_fields()
    {
        var (rows, skipped) = SellInRowParser.Parse(Response(Row()));

        Assert.Equal(0, skipped);
        var row = Assert.Single(rows);
        Assert.Equal(SellInDocumentType.Invoice, row.DocumentType);
        Assert.Equal("IN000001", row.ReferenceNumber);
        Assert.Equal(1, row.LineNumber);
        Assert.Equal(new DateOnly(2024, 3, 4), row.DocumentDate);
        Assert.Equal("012025", row.PostPeriod);
        Assert.Equal("C0001", row.CustomerId);
        Assert.Equal("Example Store", row.CustomerName);
        Assert.Equal("GP", row.Province);
        Assert.Equal("REP01", row.SalesRepId);
        Assert.Equal("Energy Bars", row.ItemClassName);
        Assert.Equal("Finished Goods", row.LocationName);
        Assert.Equal(2m, row.Quantity);
        Assert.Equal(40m, row.BaseQuantity);
        Assert.Equal(120.50m, row.Amount);
        Assert.Equal(new DateTimeOffset(2024, 3, 4, 10, 15, 0, TimeSpan.FromHours(2)), row.LastModifiedAt);
    }

    [Fact]
    public void Numbers_given_as_json_numbers_are_read_too()
    {
        var json = Response(Row().Replace("\"Amount\": \"120.50\"", "\"Amount\": 120.5", StringComparison.Ordinal));

        Assert.Equal(120.5m, Assert.Single(SellInRowParser.Parse(json).Rows).Amount);
    }

    [Fact]
    public void Rows_that_are_not_sales_lines_are_skipped_and_counted()
    {
        var json = Response(
            Row(type: "PMT"),
            Row(customer: ""),
            Row(item: ""),
            Row(type: "CRM", reference: "CM000001"));

        var (rows, skipped) = SellInRowParser.Parse(json);

        Assert.Equal(3, skipped);
        Assert.Equal(SellInDocumentType.CreditMemo, Assert.Single(rows).DocumentType);
    }

    [Fact]
    public void Missing_rep_is_kept_as_unknown()
    {
        Assert.Null(Assert.Single(SellInRowParser.Parse(Response(Row(rep: null))).Rows).SalesRepId);
    }

    [Fact]
    public void A_value_that_cannot_be_read_fails_the_whole_response()
    {
        var json = Response(Row(), Row(line: 2, amount: "12,50"));

        var exception = Assert.Throws<SellInReadException>(() => SellInRowParser.Parse(json));
        Assert.Contains("Row 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_partial_page_is_refused()
    {
        const string json = """{ "value": [], "odata.nextLink": "https://example.invalid/next" }""";

        Assert.Throws<SellInReadException>(() => SellInRowParser.Parse(json));
    }

    [Fact]
    public void A_response_without_rows_array_is_refused()
    {
        Assert.Throws<SellInReadException>(() => SellInRowParser.Parse("""{ "error": "x" }"""));
    }

    [Fact]
    public void Verbose_odata_dates_are_read()
    {
        // 2024-03-04T00:00:00 in South Africa is 2024-03-03T22:00:00Z.
        var json = Response(Row(date: "/Date(1709503200000)/"));

        Assert.Equal(new DateOnly(2024, 3, 4), Assert.Single(SellInRowParser.Parse(json).Rows).DocumentDate);
    }
}
