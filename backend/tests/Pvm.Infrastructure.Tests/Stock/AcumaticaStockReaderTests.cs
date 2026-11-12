using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pvm.Infrastructure.Acumatica;
using Pvm.Infrastructure.Stock;
using Xunit;

namespace Pvm.Infrastructure.Tests.Stock;

public sealed class AcumaticaStockReaderTests
{
    private const string Entity = "/entity/Default/24.200.001/";
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reads_allocation_figures_for_each_visible_item_only()
    {
        var acumatica = new FakeAcumatica();
        acumatica.Allocation["KLER5"] = Allocation(onHand: 40, booked: 3, allocated: 2);

        var snapshot = await Reader(acumatica).ReadAsync(["KLER5", "DYNA32"], [], CancellationToken.None);

        Assert.Contains("KLER5", snapshot.Visible);
        Assert.DoesNotContain("DYNA32", snapshot.Visible);
        var figures = snapshot.Figures["KLER5"];
        Assert.Equal(40m, figures.OnHand);
        Assert.Equal(3m, figures.SalesOrderBooked);
        Assert.Equal(2m, figures.SalesOrderAllocated);
        Assert.False(snapshot.Figures.ContainsKey("DYNA32"));

        // The inquiry asks for the configured warehouse and location.
        var inquiry = JsonDocument.Parse(Assert.Single(acumatica.AllocationBodies)).RootElement;
        Assert.Equal("KLER5", inquiry.GetProperty("InventoryID").GetProperty("value").GetString());
        Assert.Equal("HO", inquiry.GetProperty("WarehouseID").GetProperty("value").GetString());
        Assert.Equal("FG", inquiry.GetProperty("Location").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Transfers_count_only_when_both_warehouse_and_location_match()
    {
        var acumatica = new FakeAcumatica();
        acumatica.Allocation["KLER5"] = Allocation(10, 0, 0);
        acumatica.Transfers = """
            [{"FromWarehouseID":{"value":"HO"},"ToWarehouseID":{"value":"CT"},"Details":[
                {"InventoryID":{"value":"KLER5"},"Qty":{"value":4},"FromLocationID":{"value":"FG"},"ToLocationID":{"value":"MAIN"}},
                {"InventoryID":{"value":"KLER5"},"Qty":{"value":100},"FromLocationID":{"value":"RAW"},"ToLocationID":{"value":"MAIN"}}]},
             {"FromWarehouseID":{"value":"CT"},"ToWarehouseID":{"value":"HO"},"Details":[
                {"InventoryID":{"value":"KLER5"},"Qty":{"value":7},"FromLocationID":{"value":"MAIN"},"ToLocationID":{"value":"FG"}},
                {"InventoryID":{"value":"KLER5"},"Qty":{"value":200},"FromLocationID":{"value":"MAIN"},"ToLocationID":{"value":"QC"}}]}]
            """;

        var snapshot = await Reader(acumatica).ReadAsync(["KLER5"], [], CancellationToken.None);

        Assert.Equal(4m, snapshot.Figures["KLER5"].TransfersOut);
        Assert.Equal(7m, snapshot.Figures["KLER5"].TransfersIn);
        Assert.Contains("Status ne 'Released'", acumatica.TransferQuery, StringComparison.Ordinal);
        Assert.Contains(
            "$select=FromWarehouseID,ToWarehouseID,Details/InventoryID,Details/Qty,Details/FromLocationID,Details/ToLocationID",
            acumatica.TransferQuery,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_orders_skip_excluded_customers_and_orders_with_a_shipment()
    {
        var acumatica = new FakeAcumatica();
        acumatica.Allocation["SION10"] = Allocation(50, 0, 0);
        acumatica.SalesOrdersByType["SO"] = """
            [{"CustomerID":{"value":"DEB500"},"Shipments":[],"Details":[{"InventoryID":{"value":"SION10"},"OrderQty":{"value":6}}]},
             {"CustomerID":{"value":"DEB690"},"Shipments":[],"Details":[{"InventoryID":{"value":"SION10"},"OrderQty":{"value":90}}]},
             {"CustomerID":{"value":"DEB501"},"Shipments":[{"ShipmentNbr":{"value":"SH001"}}],"Details":[{"InventoryID":{"value":"SION10"},"OrderQty":{"value":80}}]}]
            """;
        acumatica.SalesOrdersByType["S1"] = """
            [{"CustomerID":{"value":"DEB502"},"Details":[{"InventoryID":{"value":"SION10"},"OrderQty":{"value":2}}]}]
            """;

        var snapshot = await Reader(acumatica, excluded: ["DEB690"]).ReadAsync(["SION10"], [], CancellationToken.None);

        Assert.Equal(8m, snapshot.Figures["SION10"].OpenOrderQuantity);
    }

    [Fact]
    public async Task Open_orders_are_filtered_by_type_and_date_in_the_Acumatica_time_zone()
    {
        var acumatica = new FakeAcumatica();

        await Reader(acumatica).ReadAsync([], [], CancellationToken.None);

        // 12 November less 45 days is 28 September, from midnight South African time.
        Assert.Equal(2, acumatica.OpenOrderQueries.Count);
        Assert.Contains(
            "$filter=OrderType eq 'SO' and Date ge datetimeoffset'2026-09-28T00:00:00+02:00'",
            acumatica.OpenOrderQueries[0],
            StringComparison.Ordinal);
        Assert.Contains("OrderType eq 'S1'", acumatica.OpenOrderQueries[1], StringComparison.Ordinal);
        Assert.Contains("$expand=Details,Shipments", acumatica.OpenOrderQueries[0], StringComparison.Ordinal);

        // Only the fields the sync uses: the full orders ran the job out of memory.
        Assert.Contains(
            "$select=OrderNbr,CustomerID,Details/InventoryID,Details/OrderQty,Shipments/ShipmentNbr",
            acumatica.OpenOrderQueries[0],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Web_orders_are_matched_in_batches_of_fifteen_with_quotes_escaped()
    {
        var acumatica = new FakeAcumatica
        {
            LoadedWebOrders = """
                [{"CustomerOrder":{"value":"5001"},"Details":[{"InventoryID":{"value":"ENER1"},"OrderQty":{"value":2}}]}]
                """,
        };
        var webOrderIds = Enumerable.Range(5001, 16).Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
        webOrderIds[15] = "O'Brien";

        var snapshot = await Reader(acumatica).ReadAsync([], webOrderIds, CancellationToken.None);

        Assert.Equal(2, acumatica.WebOrderQueries.Count);
        Assert.Equal(15, CountOf(acumatica.WebOrderQueries[0], "CustomerOrder eq '"));
        Assert.Contains("CustomerOrder eq 'O''Brien'", acumatica.WebOrderQueries[1], StringComparison.Ordinal);
        Assert.Equal(1, CountOf(acumatica.WebOrderQueries[1], "CustomerOrder eq '"));
        Assert.Contains("$select=CustomerOrder,Details/InventoryID,Details/OrderQty", acumatica.WebOrderQueries[0], StringComparison.Ordinal);
        Assert.Contains(snapshot.LoadedWebOrderLines, line => line.WebOrderId == "5001" && line.InventoryCd == "ENER1" && line.Quantity == 2m);
    }

    [Fact]
    public async Task An_allocation_error_is_a_failure_for_that_item_and_the_run_goes_on()
    {
        var acumatica = new FakeAcumatica();
        acumatica.Allocation["KLER5"] = Allocation(10, 0, 0);
        acumatica.Allocation["SION10"] = null;

        var snapshot = await Reader(acumatica).ReadAsync(["SION10", "KLER5"], [], CancellationToken.None);

        Assert.Equal("HTTP 500", snapshot.Failures["SION10"]);
        Assert.False(snapshot.Figures.ContainsKey("SION10"));
        Assert.Equal(10m, snapshot.Figures["KLER5"].OnHand);
    }

    [Fact]
    public async Task A_missing_allocation_figure_is_a_failure_not_a_zero()
    {
        var acumatica = new FakeAcumatica();
        acumatica.Allocation["KLER5"] = """{"OnHand":{},"SOBooked":{"value":0},"SOAllocated":{"value":0}}""";

        var snapshot = await Reader(acumatica).ReadAsync(["KLER5"], [], CancellationToken.None);

        Assert.False(snapshot.Figures.ContainsKey("KLER5"));
        Assert.Equal("Acumatica returned no OnHand", snapshot.Failures["KLER5"]);
    }

    [Fact]
    public async Task A_list_that_reaches_its_limit_stops_the_run_and_signs_out()
    {
        var acumatica = new FakeAcumatica { TransferCount = 10000 };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Reader(acumatica).ReadAsync(["KLER5"], [], CancellationToken.None));

        Assert.Contains("may be incomplete", failure.Message, StringComparison.Ordinal);
        Assert.Contains("$top=10000", acumatica.TransferQuery, StringComparison.Ordinal);
        Assert.Equal(1, acumatica.SignOuts);
    }

    [Fact]
    public async Task Signs_in_with_the_cookie_and_always_signs_out()
    {
        var acumatica = new FakeAcumatica { FailStockItems = true };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Reader(acumatica).ReadAsync(["KLER5"], [], CancellationToken.None));

        Assert.Equal(1, acumatica.SignOuts);
        Assert.All(acumatica.CookiesSeen, cookie => Assert.Equal(".ASPXAUTH=session1", cookie));
    }

    private static int CountOf(string text, string part)
        => (text.Length - text.Replace(part, string.Empty, StringComparison.Ordinal).Length) / part.Length;

    private static string Allocation(decimal onHand, decimal booked, decimal allocated)
        => $$$"""{"OnHand":{"value":{{{onHand}}}},"SOBooked":{"value":{{{booked}}}},"SOAllocated":{"value":{{{allocated}}}}}""";

    private static AcumaticaStockReader Reader(FakeAcumatica acumatica, string[]? excluded = null)
        => new(
            new HttpClient(acumatica),
            Options.Create(new AcumaticaOptions
            {
                BaseUrl = "https://acumatica.example/",
                Username = "integration",
                Password = "not-a-real-password",
            }),
            Options.Create(new StockSyncOptions { ExcludedCustomers = excluded ?? [] }),
            new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Answers the contract API calls the reader makes, and records what it was asked.</summary>
    private sealed class FakeAcumatica : HttpMessageHandler
    {
        public Dictionary<string, string?> Allocation { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> SalesOrdersByType { get; } = new(StringComparer.Ordinal);

        public string Transfers { get; set; } = "[]";

        public int TransferCount { get; init; }

        public string LoadedWebOrders { get; init; } = "[]";

        public bool FailStockItems { get; init; }

        public List<string> AllocationBodies { get; } = [];

        public List<string> OpenOrderQueries { get; } = [];

        public List<string> WebOrderQueries { get; } = [];

        public List<string> CookiesSeen { get; } = [];

        public string TransferQuery { get; private set; } = string.Empty;

        public int SignOuts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            if (path == "/entity/auth/login")
            {
                var login = new HttpResponseMessage(HttpStatusCode.NoContent);
                login.Headers.Add("Set-Cookie", ".ASPXAUTH=session1; path=/; HttpOnly");
                return login;
            }

            if (request.Headers.TryGetValues("Cookie", out var cookies))
            {
                CookiesSeen.AddRange(cookies);
            }

            if (path == "/entity/auth/logout")
            {
                SignOuts++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (path == Entity + "StockItem")
            {
                return FailStockItems
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : Json("""[{"InventoryID":{"value":"KLER5 "}},{"InventoryID":{"value":"SION10"}}]""");
            }

            if (path == Entity + "TransferOrder")
            {
                TransferQuery = query;
                return TransferCount > 0
                    ? Json("[" + string.Join(",", Enumerable.Repeat("{}", TransferCount)) + "]")
                    : Json(Transfers);
            }

            if (path == Entity + "SalesOrder" && query.Contains("CustomerOrder eq", StringComparison.Ordinal))
            {
                WebOrderQueries.Add(query);
                return Json(WebOrderQueries.Count == 1 ? LoadedWebOrders : "[]");
            }

            if (path == Entity + "SalesOrder")
            {
                OpenOrderQueries.Add(query);
                var type = SalesOrdersByType.Keys.FirstOrDefault(key => query.Contains($"OrderType eq '{key}'", StringComparison.Ordinal));
                return Json(type is null ? "[]" : SalesOrdersByType[type]);
            }

            if (path == Entity + "InventoryAllocationInquiry" && request.Method == HttpMethod.Put)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                AllocationBodies.Add(body);
                var item = JsonDocument.Parse(body).RootElement.GetProperty("InventoryID").GetProperty("value").GetString()!;
                return Allocation.TryGetValue(item, out var answer) && answer is not null
                    ? Json(answer)
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body)
            => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
