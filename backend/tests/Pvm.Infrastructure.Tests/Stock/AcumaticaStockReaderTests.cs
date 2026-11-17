using System.Globalization;
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
    private const string Dac = "/t/PVM/api/odata/dac/";

    // 08:00 in South Africa on 17 November 2026.
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reads_on_hand_at_the_location_for_each_visible_item_only()
    {
        var acumatica = new FakeAcumatica();
        acumatica.OnHand["CER07"] = 3307;

        var snapshot = await Reader(acumatica).ReadAsync(["CER07", "DYNA32"], [], CancellationToken.None);

        Assert.Contains("CER07", snapshot.Visible);
        Assert.DoesNotContain("DYNA32", snapshot.Visible);
        Assert.Equal(3307m, snapshot.Figures["CER07"].OnHand);
        Assert.False(snapshot.Figures.ContainsKey("DYNA32"));
        var inquiry = JsonDocument.Parse(Assert.Single(acumatica.InquiryBodies)).RootElement;
        Assert.Equal("HO", inquiry.GetProperty("WarehouseID").GetProperty("value").GetString());
        Assert.Equal("FG", inquiry.GetProperty("Location").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Held_back_adjustments_count_on_hold_ones_and_leave_out_stock_takes()
    {
        var acumatica = new FakeAcumatica
        {
            Adjustments = """
                [{"ReferenceNbr":{"value":"012652"},"Date":{"value":"2026-10-28T00:00:00+02:00"},"Details":[
                    {"InventoryID":{"value":"CER07"},"WarehouseID":{"value":"HO"},"Qty":{"value":3000},"UOM":{"value":"GRAM"},"ReasonCode":{"value":"BLADJHO"}},
                    {"InventoryID":{"value":"CER07"},"WarehouseID":{"value":"KZN"},"Qty":{"value":50},"UOM":{"value":"GRAM"},"ReasonCode":{"value":"BLADJHO"}}]},
                 {"ReferenceNbr":{"value":"012668"},"Date":{"value":"2026-10-30T00:00:00+02:00"},"Details":[
                    {"InventoryID":{"value":"CER07"},"WarehouseID":{"value":"HO"},"Qty":{"value":-200},"UOM":{"value":"GRAM"},"ReasonCode":{"value":"BLADJHO"}}]},
                 {"ReferenceNbr":{"value":"000882"},"Date":{"value":"2022-05-03T00:00:00+02:00"},"Details":[
                    {"InventoryID":{"value":"CER07"},"WarehouseID":{"value":"HO"},"Qty":{"value":406},"UOM":{"value":"GRAM"},"ReasonCode":{"value":"BLADJHO"}}]},
                 {"ReferenceNbr":{"value":"012689"},"Date":{"value":"2026-11-02T00:00:00+02:00"},"Details":[
                    {"InventoryID":{"value":"CER07"},"WarehouseID":{"value":"HO"},"Qty":{"value":-200},"UOM":{"value":"GRAM"},"ReasonCode":{"value":"INADJME"}}]}]
                """,
        };
        acumatica.OnHand["CER07"] = 3307;
        acumatica.StockTakeRegisters.Add(("000882", "R"));

        var snapshot = await Reader(acumatica).ReadAsync(["CER07"], [], CancellationToken.None);

        // 3000 - 200: the on-hold 012668 counts, the stock take 000882 and other reasons do not.
        Assert.Equal(2800m, snapshot.Figures["CER07"].HeldBackAdjustments);
        Assert.Contains("Date ge datetimeoffset'2022-05-01T00:00:00+02:00'", acumatica.AdjustmentQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Auto_adjustments_count_after_the_last_stock_count_in_base_units()
    {
        var acumatica = new FakeAcumatica
        {
            Adjustments = """
                [{"ReferenceNbr":{"value":"012669"},"Date":{"value":"2026-10-30T00:00:00+02:00"},"Details":[
                    {"InventoryID":{"value":"CER07"},"WarehouseID":{"value":"HO"},"Qty":{"value":1},"UOM":{"value":"OUTER5"},"ReasonCode":{"value":"AUTOADJ"}}]},
                 {"ReferenceNbr":{"value":"012737"},"Date":{"value":"2026-11-03T00:00:00+02:00"},"Details":[
                    {"InventoryID":{"value":"CER07"},"WarehouseID":{"value":"HO"},"Qty":{"value":1},"UOM":{"value":"OUTER5"},"ReasonCode":{"value":"AUTOADJ"}}]},
                 {"ReferenceNbr":{"value":"012943"},"Date":{"value":"2026-11-09T00:00:00+02:00"},"Details":[
                    {"InventoryID":{"value":"CER07"},"WarehouseID":{"value":"HO"},"Qty":{"value":2},"UOM":{"value":"GRAM"},"ReasonCode":{"value":"AUTOADJ"}}]}]
                """,
            StockTakes = """
                [{"ReferenceNbr":{"value":"PI001400"},"Details":[{"InventoryID":{"value":"CER07"}}]},
                 {"ReferenceNbr":{"value":"PI001450"},"Details":[{"InventoryID":{"value":"CER07"}}]}]
                """,
        };
        acumatica.OnHand["CER07"] = 3307;
        acumatica.CountDates["PI001400"] = "2026-09-30T00:00:00Z";
        // The same day in the two formats Acumatica uses.
        acumatica.CountDates["PI001450"] = "2026-10-30T00:00:00+02:00";

        var snapshot = await Reader(acumatica).ReadAsync(["CER07"], [], CancellationToken.None);

        // Counted on 30 October: the adjustment of that day does not count, the later ones do,
        // and 1 OUTER5 is 5 base units.
        Assert.Equal(7m, snapshot.Figures["CER07"].AutoAdjustments);
    }

    [Fact]
    public async Task An_item_never_counted_has_no_auto_adjustments()
    {
        var acumatica = new FakeAcumatica
        {
            Adjustments = """
                [{"ReferenceNbr":{"value":"012737"},"Date":{"value":"2026-11-03T00:00:00+02:00"},"Details":[
                    {"InventoryID":{"value":"CER07"},"WarehouseID":{"value":"HO"},"Qty":{"value":1},"UOM":{"value":"GRAM"},"ReasonCode":{"value":"AUTOADJ"}}]}]
                """,
        };
        acumatica.OnHand["CER07"] = 10;

        var snapshot = await Reader(acumatica).ReadAsync(["CER07"], [], CancellationToken.None);

        Assert.Equal(0m, snapshot.Figures["CER07"].AutoAdjustments);
    }

    [Fact]
    public async Task An_adjustment_unit_with_no_conversion_is_a_failure_for_that_item()
    {
        var acumatica = new FakeAcumatica
        {
            Adjustments = """
                [{"ReferenceNbr":{"value":"012652"},"Date":{"value":"2026-10-28T00:00:00+02:00"},"Details":[
                    {"InventoryID":{"value":"CER07"},"WarehouseID":{"value":"HO"},"Qty":{"value":1},"UOM":{"value":"PALLET"},"ReasonCode":{"value":"BLADJHO"}}]}]
                """,
        };
        acumatica.OnHand["CER07"] = 10;

        var snapshot = await Reader(acumatica).ReadAsync(["CER07"], [], CancellationToken.None);

        Assert.False(snapshot.Figures.ContainsKey("CER07"));
        Assert.Contains("PALLET", snapshot.Failures["CER07"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_orders_count_unconfirmed_shipments_and_skip_excluded_customers_and_other_warehouses()
    {
        var acumatica = new FakeAcumatica();
        acumatica.OnHand["SION10"] = 50;
        acumatica.SalesOrdersByType["SO"] = """
            [{"OrderNbr":{"value":"SO1"},"CustomerID":{"value":"DEB500"},"Date":{"value":"2026-11-10T00:00:00+02:00"},"Details":[
                {"InventoryID":{"value":"SION10"},"OrderQty":{"value":6},"WarehouseID":{"value":"HO"}},
                {"InventoryID":{"value":"SION10"},"OrderQty":{"value":40},"WarehouseID":{"value":"KZN"}}]},
             {"OrderNbr":{"value":"SO2"},"CustomerID":{"value":"DEB690"},"Date":{"value":"2026-11-10T00:00:00+02:00"},"Details":[
                {"InventoryID":{"value":"SION10"},"OrderQty":{"value":90},"WarehouseID":{"value":"HO"}}]},
             {"OrderNbr":{"value":"SO3"},"CustomerID":{"value":"DEB501"},"Date":{"value":"2026-11-10T00:00:00+02:00"},"Details":[
                {"InventoryID":{"value":"SION10"},"OrderQty":{"value":80},"WarehouseID":{"value":"HO"}}]},
             {"OrderNbr":{"value":"SO4"},"CustomerID":{"value":"DEB502"},"Date":{"value":"2026-11-10T00:00:00+02:00"},"Details":[
                {"InventoryID":{"value":"SION10"},"OrderQty":{"value":3},"WarehouseID":{"value":"HO"}}]}]
            """;
        acumatica.Shipments.Add(("SO3", true));
        acumatica.Shipments.Add(("SO4", false));
        acumatica.Shipments.Add(("SO4", true));

        var snapshot = await Reader(acumatica, excluded: ["DEB690"]).ReadAsync(["SION10"], [], CancellationToken.None);

        // SO1: 6 at HO, no shipment. SO2: excluded customer. SO3: all shipments confirmed.
        // SO4: one unconfirmed shipment, so its 3 count once.
        Assert.Equal(9m, snapshot.Figures["SION10"].OpenOrderQuantity);
    }

    [Fact]
    public void An_order_counts_once_for_each_unconfirmed_shipment_as_in_the_sheet()
        => Assert.Equal(2, AcumaticaStockReader.TimesCounted(
            new Dictionary<string, (int All, int Unconfirmed)> { ["SO9"] = (3, 2) }, "SO9"));

    [Fact]
    public async Task The_order_window_ends_at_the_same_time_of_day_as_the_sheet()
    {
        var acumatica = new FakeAcumatica();
        acumatica.OnHand["SION10"] = 50;
        // 45 days before 08:00 on 17 November is 08:00 on 3 October: an order dated
        // 3 October (midnight) is before that, so it does not count; 4 October does.
        acumatica.SalesOrdersByType["SO"] = """
            [{"OrderNbr":{"value":"SO1"},"CustomerID":{"value":"DEB500"},"Date":{"value":"2026-10-03T00:00:00+02:00"},"Details":[
                {"InventoryID":{"value":"SION10"},"OrderQty":{"value":5},"WarehouseID":{"value":"HO"}}]},
             {"OrderNbr":{"value":"SO2"},"CustomerID":{"value":"DEB500"},"Date":{"value":"2026-10-04T00:00:00+02:00"},"Details":[
                {"InventoryID":{"value":"SION10"},"OrderQty":{"value":2},"WarehouseID":{"value":"HO"}}]}]
            """;

        var snapshot = await Reader(acumatica).ReadAsync(["SION10"], [], CancellationToken.None);

        Assert.Equal(2m, snapshot.Figures["SION10"].OpenOrderQuantity);
        Assert.Contains("OrderType eq 'SO' and Date ge datetimeoffset'2026-10-03T00:00:00+02:00'", acumatica.OrderQueries[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Promo_orders_are_read_by_customer_in_batches_and_skip_quotes()
    {
        var acumatica = new FakeAcumatica
        {
            PromoOrders = """
                [{"OrderNbr":{"value":"P1"},"CustomerID":{"value":"DEB1"},"Date":{"value":"2026-09-01T00:00:00+02:00"},"Details":[
                    {"InventoryID":{"value":"ENER10"},"OrderQty":{"value":24},"WarehouseID":{"value":"HO"}}]}]
                """,
        };
        acumatica.OnHand["ENER10"] = 100;
        var promo = Enumerable.Range(1, 16).Select(i => $"DEB{i}").ToArray();

        var snapshot = await Reader(acumatica, promo: promo).ReadAsync(["ENER10"], [], CancellationToken.None);

        Assert.Equal(24m, snapshot.Figures["ENER10"].PromoOrderQuantity);
        Assert.Equal(2, acumatica.PromoQueries.Count);
        Assert.Contains("OrderType ne 'QT'", acumatica.PromoQueries[0], StringComparison.Ordinal);
        Assert.Contains("Date ge datetimeoffset'2026-06-20T00:00:00+02:00'", acumatica.PromoQueries[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_transfers_out_count_and_warehouse_rule_items_count_every_location()
    {
        var acumatica = new FakeAcumatica
        {
            Transfers = """
                [{"FromWarehouseID":{"value":"HO"},"Details":[
                    {"InventoryID":{"value":"KLER5"},"Qty":{"value":4},"FromLocationID":{"value":"FG"}},
                    {"InventoryID":{"value":"KLER5"},"Qty":{"value":100},"FromLocationID":{"value":"RAW"}},
                    {"InventoryID":{"value":"PROT21"},"Qty":{"value":2},"FromLocationID":{"value":"FG"}},
                    {"InventoryID":{"value":"PROT21"},"Qty":{"value":4},"FromLocationID":{"value":"EXP"}}]},
                 {"FromWarehouseID":{"value":"CT"},"Details":[
                    {"InventoryID":{"value":"KLER5"},"Qty":{"value":7},"FromLocationID":{"value":"FG"}}]}]
                """,
        };
        acumatica.OnHand["KLER5"] = 10;
        acumatica.OnHand["PROT21"] = 21;
        acumatica.Warehouse["PROT21"] = (5, 12);

        var snapshot = await Reader(acumatica).ReadAsync(["KLER5", "PROT21"], [], CancellationToken.None);

        Assert.Equal(4m, snapshot.Figures["KLER5"].TransfersOut);
        Assert.Equal(6m, snapshot.Figures["PROT21"].TransfersOut);
        Assert.Null(snapshot.Figures["KLER5"].WarehouseCommitted);
        Assert.Equal(12m, snapshot.Figures["PROT21"].WarehouseCommitted);
    }

    [Fact]
    public async Task A_missing_on_hand_figure_is_a_failure_not_a_zero()
    {
        var acumatica = new FakeAcumatica();
        acumatica.RawInquiry["CER07"] = """{"OnHand":{}}""";

        var snapshot = await Reader(acumatica).ReadAsync(["CER07"], [], CancellationToken.None);

        Assert.False(snapshot.Figures.ContainsKey("CER07"));
        Assert.Equal("Acumatica returned no OnHand", snapshot.Failures["CER07"]);
    }

    [Fact]
    public async Task An_inquiry_error_is_a_failure_for_that_item_and_the_run_goes_on()
    {
        var acumatica = new FakeAcumatica();
        acumatica.OnHand["CER07"] = 10;

        var snapshot = await Reader(acumatica).ReadAsync(["SION10", "CER07"], [], CancellationToken.None);

        Assert.Equal("HTTP 500", snapshot.Failures["SION10"]);
        Assert.Equal(10m, snapshot.Figures["CER07"].OnHand);
    }

    [Fact]
    public async Task Table_reads_use_basic_authentication_and_follow_next_links()
    {
        var acumatica = new FakeAcumatica { SplitRegisters = true };
        acumatica.OnHand["CER07"] = 10;
        acumatica.StockTakeRegisters.Add(("000882", "R"));
        acumatica.StockTakeRegisters.Add(("000883", "R"));

        await Reader(acumatica).ReadAsync(["CER07"], [], CancellationToken.None);

        Assert.Equal(2, acumatica.RegisterPages);
        Assert.All(acumatica.DacAuthorizations, header => Assert.StartsWith("Basic ", header, StringComparison.Ordinal));
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(acumatica.DacAuthorizations[0]["Basic ".Length..]));
        Assert.Equal("integration@PVM:not-a-real-password", decoded);
    }

    [Fact]
    public async Task A_refused_table_read_stops_the_run_and_signs_out()
    {
        var acumatica = new FakeAcumatica { RefuseDac = true };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Reader(acumatica).ReadAsync(["CER07"], [], CancellationToken.None));

        Assert.Contains("read access", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, acumatica.SignOuts);
    }

    [Fact]
    public async Task A_list_that_reaches_its_limit_stops_the_run_and_signs_out()
    {
        var acumatica = new FakeAcumatica { TransferCount = 10000 };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Reader(acumatica).ReadAsync(["CER07"], [], CancellationToken.None));

        Assert.Contains("may be incomplete", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, acumatica.SignOuts);
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
        var webOrderIds = Enumerable.Range(5001, 16).Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList();
        webOrderIds[15] = "O'Brien";

        var snapshot = await Reader(acumatica).ReadAsync([], webOrderIds, CancellationToken.None);

        Assert.Equal(2, acumatica.WebOrderQueries.Count);
        Assert.Contains("CustomerOrder eq 'O''Brien'", acumatica.WebOrderQueries[1], StringComparison.Ordinal);
        Assert.Contains(snapshot.LoadedWebOrderLines, line => line.WebOrderId == "5001" && line.InventoryCd == "ENER1" && line.Quantity == 2m);
    }

    [Fact]
    public async Task Signs_in_with_the_cookie_and_always_signs_out()
    {
        var acumatica = new FakeAcumatica { FailStockItems = true };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Reader(acumatica).ReadAsync(["CER07"], [], CancellationToken.None));

        Assert.Equal(1, acumatica.SignOuts);
        Assert.All(acumatica.CookiesSeen, cookie => Assert.Equal(".ASPXAUTH=session1", cookie));
    }

    private static AcumaticaStockReader Reader(FakeAcumatica acumatica, string[]? excluded = null, string[]? promo = null)
        => new(
            new HttpClient(acumatica),
            Options.Create(new AcumaticaOptions
            {
                BaseUrl = "https://acumatica.example/",
                Company = "PVM",
                Username = "integration",
                Password = "not-a-real-password",
            }),
            Options.Create(new StockSyncOptions { ExcludedCustomers = excluded ?? [], PromoCustomers = promo ?? [] }),
            new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Answers the contract and table API calls the reader makes, and records what it was asked.</summary>
    private sealed class FakeAcumatica : HttpMessageHandler
    {
        public Dictionary<string, decimal> OnHand { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> RawInquiry { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, (decimal Booked, decimal Allocated)> Warehouse { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> SalesOrdersByType { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> CountDates { get; } = new(StringComparer.Ordinal);

        public List<(string RefNbr, string Status)> StockTakeRegisters { get; } = [];

        public List<(string OrderNbr, bool Confirmed)> Shipments { get; } = [];

        public string Adjustments { get; init; } = "[]";

        public string StockTakes { get; init; } = "[]";

        public string Transfers { get; init; } = "[]";

        public string PromoOrders { get; init; } = "[]";

        public string LoadedWebOrders { get; init; } = "[]";

        public int TransferCount { get; init; }

        public bool FailStockItems { get; init; }

        public bool RefuseDac { get; init; }

        public bool SplitRegisters { get; init; }

        public List<string> InquiryBodies { get; } = [];

        public List<string> OrderQueries { get; } = [];

        public List<string> PromoQueries { get; } = [];

        public List<string> WebOrderQueries { get; } = [];

        public List<string> CookiesSeen { get; } = [];

        public List<string> DacAuthorizations { get; } = [];

        public string AdjustmentQuery { get; private set; } = string.Empty;

        public int RegisterPages { get; private set; }

        public int SignOuts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            if (path.StartsWith(Dac, StringComparison.Ordinal))
            {
                return Table(request, path[Dac.Length..], query);
            }

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

            switch (path[Math.Min(path.Length, Entity.Length)..])
            {
                case "" when path == "/entity/auth/logout":
                    SignOuts++;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                case "StockItem":
                    return FailStockItems
                        ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                        : Json("""
                            [{"InventoryID":{"value":"CER07 "},"BaseUOM":{"value":"GRAM"},"UOMConversions":[
                                {"FromUOM":{"value":"OUTER5"},"ToUOM":{"value":"GRAM"},"MultiplyOrDivide":{"value":"Multiply"},"ConversionFactor":{"value":5}}]},
                             {"InventoryID":{"value":"SION10"},"BaseUOM":{"value":"EA"}},
                             {"InventoryID":{"value":"KLER5"},"BaseUOM":{"value":"EA"}},
                             {"InventoryID":{"value":"ENER10"},"BaseUOM":{"value":"EA"}},
                             {"InventoryID":{"value":"PROT21"},"BaseUOM":{"value":"EA"}}]
                            """);
                case "InventoryAdjustment":
                    AdjustmentQuery = query;
                    return Json(Adjustments);
                case "PhysicalInventoryReview":
                    return Json(StockTakes);
                case "TransferOrder":
                    return TransferCount > 0
                        ? Json("[" + string.Join(",", Enumerable.Repeat("{}", TransferCount)) + "]")
                        : Json(Transfers);
                case "SalesOrder" when query.Contains("CustomerOrder eq", StringComparison.Ordinal):
                    WebOrderQueries.Add(query);
                    return Json(WebOrderQueries.Count == 1 ? LoadedWebOrders : "[]");
                case "SalesOrder" when query.Contains("OrderType ne", StringComparison.Ordinal):
                    PromoQueries.Add(query);
                    return Json(PromoQueries.Count == 1 ? PromoOrders : "[]");
                case "SalesOrder":
                    OrderQueries.Add(query);
                    var type = SalesOrdersByType.Keys.FirstOrDefault(key => query.Contains($"OrderType eq '{key}'", StringComparison.Ordinal));
                    return Json(type is null ? "[]" : SalesOrdersByType[type]);
                case "InventoryAllocationInquiry" when request.Method == HttpMethod.Put:
                    var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                    var inquiry = JsonDocument.Parse(body).RootElement;
                    var item = inquiry.GetProperty("InventoryID").GetProperty("value").GetString()!;
                    if (!inquiry.TryGetProperty("Location", out _))
                    {
                        var (booked, allocated) = Warehouse.GetValueOrDefault(item);
                        return Json($$$"""{"SOBooked":{"value":{{{booked}}}},"SOAllocated":{"value":{{{allocated}}}}}""");
                    }

                    InquiryBodies.Add(body);
                    if (RawInquiry.TryGetValue(item, out var raw))
                    {
                        return Json(raw);
                    }

                    return OnHand.TryGetValue(item, out var onHand)
                        ? Json($$$"""{"OnHand":{"value":{{{onHand}}}}}""")
                        : new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private HttpResponseMessage Table(HttpRequestMessage request, string table, string query)
        {
            DacAuthorizations.Add(request.Headers.Authorization!.ToString());
            if (RefuseDac)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            switch (table)
            {
                case "INRegister":
                    RegisterPages++;
                    var rows = StockTakeRegisters.Select(r => $$"""{"RefNbr":"{{r.RefNbr}}","Status":"{{r.Status}}"}""").ToList();
                    if (SplitRegisters && RegisterPages == 1)
                    {
                        return Json($$"""{"value":[{{rows[0]}}],"@odata.nextLink":"https://acumatica.example/t/PVM/api/odata/dac/INRegister?$skiptoken=1"}""");
                    }

                    return Json($$"""{"value":[{{string.Join(",", SplitRegisters ? rows.Skip(1) : rows)}}]}""");
                case "INPIHeader":
                    return Json("{\"value\":[" + string.Join(",", CountDates.Select(c => $$"""{"PIID":"{{c.Key}}","CountDate":"{{c.Value}}"}""")) + "]}");
                case "SOOrderShipment":
                    return Json("{\"value\":[" + string.Join(",", Shipments.Select(s => $$"""{"OrderNbr":"{{s.OrderNbr}}","Confirmed":{{(s.Confirmed ? "true" : "false")}}}""")) + "]}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body)
            => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
