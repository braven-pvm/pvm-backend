using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pvm.Application.Stock;
using Pvm.Infrastructure.Acumatica;

namespace Pvm.Infrastructure.Stock;

/// <summary>
/// Reads the figures of the "current stock" sheet in inventory lite.xlsm from Acumatica, with no
/// direct SQL. Most reads use the contract REST API in one signed-in session. Three facts the
/// contract API does not give come from the table-level OData API (<c>/t/{company}/api/odata/dac</c>):
/// which adjustments a stock take made (INRegister), stock-count dates (INPIHeader) and whether a
/// shipment is confirmed (SOOrderShipment).
/// </summary>
public sealed class AcumaticaStockReader(
    HttpClient httpClient,
    IOptions<AcumaticaOptions> acumaticaOptions,
    IOptions<StockSyncOptions> stockOptions,
    TimeProvider timeProvider) : IAcumaticaStockReader
{
    // Acumatica rejects a long OR-chain in one filter; this many values per call is safe.
    private const int OrFilterBatchSize = 15;

    // Row caps on each list. A list that comes back full may be cut short, so the run stops.
    internal const int ListLimit = 10000;
    internal const int AdjustmentListLimit = 100000;
    internal const int WebOrderListLimit = 1000;

    // Only the fields the sync uses: the adjustment and stock-take lists are each about 17 MB even so.
    private const string StockItemFields =
        "InventoryID,BaseUOM,UOMConversions/FromUOM,UOMConversions/ToUOM,UOMConversions/MultiplyOrDivide,UOMConversions/ConversionFactor";
    private const string AdjustmentFields =
        "ReferenceNbr,Date,Details/InventoryID,Details/WarehouseID,Details/Qty,Details/UOM,Details/ReasonCode";
    private const string StockTakeFields = "ReferenceNbr,Details/InventoryID";
    private const string TransferFields = "FromWarehouseID,Details/InventoryID,Details/Qty,Details/FromLocationID";
    private const string OrderFields = "OrderNbr,CustomerID,Date,Details/InventoryID,Details/OrderQty,Details/WarehouseID";
    private const string WebOrderFields = "CustomerOrder,Details/InventoryID,Details/OrderQty";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly AcumaticaOptions _acumatica = acumaticaOptions.Value;
    private readonly StockSyncOptions _stock = stockOptions.Value;

    public async Task<AcumaticaStockSnapshot> ReadAsync(
        IReadOnlyCollection<string> inventoryCds,
        IReadOnlyCollection<string> webOrderIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inventoryCds);
        ArgumentNullException.ThrowIfNull(webOrderIds);
        if (string.IsNullOrWhiteSpace(_acumatica.BaseUrl)
            || string.IsNullOrWhiteSpace(_acumatica.Company)
            || string.IsNullOrWhiteSpace(_acumatica.Username)
            || string.IsNullOrWhiteSpace(_acumatica.Password))
        {
            throw new InvalidOperationException("Acumatica BaseUrl, Company and credentials are required for the stock sync.");
        }

        var now = LocalNow();
        var cookie = await SignInAsync(cancellationToken);
        try
        {
            var units = await ReadStockItemsAsync(cookie, cancellationToken);
            var visible = units.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var wanted = inventoryCds
                .Where(visible.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var failures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var (heldBack, auto) = await ReadAdjustmentsAsync(cookie, units, wanted, failures, cancellationToken);
            var shipments = await ReadShipmentsAsync(now, cancellationToken);
            var openOrders = await ReadOpenOrdersAsync(cookie, shipments, now, cancellationToken);
            var promoOrders = await ReadPromoOrdersAsync(cookie, shipments, now, cancellationToken);
            var (outOfLocation, outOfWarehouse) = await ReadTransfersOutAsync(cookie, cancellationToken);

            var figures = new Dictionary<string, AcumaticaStockFigures>(StringComparer.OrdinalIgnoreCase);
            foreach (var inventoryCd in wanted.Where(code => !failures.ContainsKey(code)))
            {
                var onHand = await ReadOnHandAsync(cookie, inventoryCd, cancellationToken);
                if (onHand.Error is not null)
                {
                    failures[inventoryCd] = onHand.Error;
                    continue;
                }

                var warehouseRule = IsWarehouseRule(inventoryCd);
                decimal? committed = null;
                if (warehouseRule)
                {
                    var warehouse = await ReadWarehouseCommittedAsync(cookie, inventoryCd, cancellationToken);
                    if (warehouse.Error is not null)
                    {
                        failures[inventoryCd] = warehouse.Error;
                        continue;
                    }

                    committed = warehouse.Committed;
                }

                figures[inventoryCd] = new AcumaticaStockFigures(
                    inventoryCd,
                    onHand.OnHand,
                    heldBack.GetValueOrDefault(inventoryCd),
                    auto.GetValueOrDefault(inventoryCd),
                    openOrders.GetValueOrDefault(inventoryCd),
                    promoOrders.GetValueOrDefault(inventoryCd),
                    (warehouseRule ? outOfWarehouse : outOfLocation).GetValueOrDefault(inventoryCd),
                    committed);
            }

            var loaded = await ReadLoadedWebOrdersAsync(cookie, webOrderIds, cancellationToken);
            return new AcumaticaStockSnapshot(visible, figures, failures, loaded);
        }
        finally
        {
            await SignOutAsync(cookie);
        }
    }

    private bool IsWarehouseRule(string inventoryCd)
        => _stock.WarehouseRulePrefixes.Any(prefix => inventoryCd.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Every stock item the user can see, with its base unit and unit conversions. An item hidden
    /// by a restriction group reads as zero in the inquiries; listing what the user can see is how
    /// a hidden item is told apart from one that is really out of stock.
    /// </summary>
    private async Task<Dictionary<string, ItemUnits>> ReadStockItemsAsync(string? cookie, CancellationToken cancellationToken)
    {
        var rows = await GetArrayAsync(cookie, $"StockItem?$expand=UOMConversions&$select={StockItemFields}", ListLimit, "stock item list", cancellationToken);
        var units = new Dictionary<string, ItemUnits>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var id = Text(row, "InventoryID");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var conversions = new Dictionary<string, (bool Multiply, decimal Factor)>(StringComparer.OrdinalIgnoreCase);
            foreach (var conversion in Array(row, "UOMConversions"))
            {
                var from = Text(conversion, "FromUOM");
                var factor = RequiredNumber(conversion, "ConversionFactor");
                if (!string.IsNullOrEmpty(from) && factor is > 0 && Same(Text(conversion, "ToUOM"), Text(row, "BaseUOM") ?? string.Empty))
                {
                    conversions[from] = (!Same(Text(conversion, "MultiplyOrDivide"), "Divide"), factor.Value);
                }
            }

            units[id] = new ItemUnits(Text(row, "BaseUOM") ?? string.Empty, conversions);
        }

        return units;
    }

    /// <summary>
    /// The held-back (BLADJHO) total since the cut-off and the automatic (AUTOADJ) total after the
    /// last stock count, at the warehouse, in base units. Adjustments on hold count, as in the
    /// sheet; adjustments that a released stock take made do not.
    /// </summary>
    private async Task<(Dictionary<string, decimal> HeldBack, Dictionary<string, decimal> Auto)> ReadAdjustmentsAsync(
        string? cookie,
        Dictionary<string, ItemUnits> units,
        IReadOnlyCollection<string> wanted,
        Dictionary<string, string> failures,
        CancellationToken cancellationToken)
    {
        var stockTakeAdjustments = await ReadStockTakeAdjustmentsAsync(cancellationToken);
        var lastCounts = await ReadLastCountDatesAsync(cookie, cancellationToken);
        var wantedSet = wanted.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filter = $"Date ge datetimeoffset'{_stock.HeldBackSince:yyyy-MM-dd}T00:00:00{_stock.AcumaticaUtcOffset}'";
        var rows = await GetArrayAsync(
            cookie,
            "InventoryAdjustment?$filter=" + Uri.EscapeDataString(filter) + "&$expand=Details&$select=" + AdjustmentFields,
            AdjustmentListLimit,
            "inventory adjustments",
            cancellationToken);

        var heldBack = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var auto = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var adjustment in rows)
        {
            if (stockTakeAdjustments.Contains(Text(adjustment, "ReferenceNbr") ?? string.Empty))
            {
                continue;
            }

            var date = LocalDate(Text(adjustment, "Date"));
            foreach (var line in Array(adjustment, "Details"))
            {
                var inventoryCd = Text(line, "InventoryID");
                var reason = Text(line, "ReasonCode");
                if (string.IsNullOrEmpty(inventoryCd)
                    || !wantedSet.Contains(inventoryCd)
                    || !Same(Text(line, "WarehouseID"), _stock.WarehouseId))
                {
                    continue;
                }

                var isHeldBack = Same(reason, _stock.HeldBackReasonCode);
                var isAuto = Same(reason, _stock.AutoAdjustmentReasonCode)
                    && date is not null
                    && lastCounts.TryGetValue(inventoryCd, out var lastCount)
                    && date > lastCount;
                if (!isHeldBack && !isAuto)
                {
                    continue;
                }

                var unit = Text(line, "UOM") ?? string.Empty;
                var quantity = ToBase(units, inventoryCd, unit, Number(line, "Qty"));
                if (quantity is null)
                {
                    failures[inventoryCd] = $"no conversion from {unit} to the base unit for an adjustment";
                    continue;
                }

                var totals = isHeldBack ? heldBack : auto;
                totals[inventoryCd] = totals.GetValueOrDefault(inventoryCd) + quantity.Value;
            }
        }

        return (heldBack, auto);
    }

    /// <summary>Adjustments that a released stock take made. The sheet leaves them out.</summary>
    private async Task<HashSet<string>> ReadStockTakeAdjustmentsAsync(CancellationToken cancellationToken)
    {
        var rows = await GetDacAsync("INRegister?$select=RefNbr,Status&$filter=" + Uri.EscapeDataString("DocType eq 'A' and PIID ne null"), "stock-take adjustments", cancellationToken);
        return rows
            .Where(row => Same(DacText(row, "Status"), "R"))
            .Select(row => DacText(row, "RefNbr") ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The date of the latest stock take that counted each item, at any warehouse.</summary>
    private async Task<Dictionary<string, DateOnly>> ReadLastCountDatesAsync(string? cookie, CancellationToken cancellationToken)
    {
        var countDates = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in await GetDacAsync("INPIHeader?$select=PIID,CountDate", "stock-take dates", cancellationToken))
        {
            if (DacText(row, "PIID") is { Length: > 0 } id && row.TryGetProperty("CountDate", out var value)
                && value.ValueKind == JsonValueKind.String && LocalDate(value.GetString()) is { } date)
            {
                // The calendar day as Acumatica shows it, whatever offset the value carries.
                countDates[id] = date;
            }
        }

        var stockTakes = await GetArrayAsync(cookie, $"PhysicalInventoryReview?$expand=Details&$select={StockTakeFields}", ListLimit, "stock takes", cancellationToken);
        var lastCounts = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
        foreach (var stockTake in stockTakes)
        {
            if (!countDates.TryGetValue(Text(stockTake, "ReferenceNbr") ?? string.Empty, out var countDate))
            {
                continue;
            }

            foreach (var line in Array(stockTake, "Details"))
            {
                var inventoryCd = Text(line, "InventoryID");
                if (!string.IsNullOrEmpty(inventoryCd)
                    && (!lastCounts.TryGetValue(inventoryCd, out var known) || countDate > known))
                {
                    lastCounts[inventoryCd] = countDate;
                }
            }
        }

        return lastCounts;
    }

    /// <summary>
    /// The shipments of each order number, confirmed or not. The sheet joins shipments on the order
    /// number alone and keeps one row per shipment that is not confirmed.
    /// </summary>
    private async Task<Dictionary<string, (int All, int Unconfirmed)>> ReadShipmentsAsync(DateTime now, CancellationToken cancellationToken)
    {
        // A shipment is never created before its order, so the longest order look-back is enough.
        var since = now.AddDays(-Math.Max(_stock.OpenOrderLookbackDays, _stock.PromoOrderLookbackDays) - 1);
        var rows = await GetDacAsync(
            "SOOrderShipment?$select=OrderNbr,Confirmed&$filter=" + Uri.EscapeDataString($"CreatedDateTime ge {since:yyyy-MM-dd}T00:00:00Z"),
            "order shipments",
            cancellationToken);
        var shipments = new Dictionary<string, (int All, int Unconfirmed)>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var orderNbr = DacText(row, "OrderNbr");
            if (string.IsNullOrEmpty(orderNbr))
            {
                continue;
            }

            var confirmed = row.TryGetProperty("Confirmed", out var value) && value.ValueKind == JsonValueKind.True;
            var (all, unconfirmed) = shipments.GetValueOrDefault(orderNbr);
            shipments[orderNbr] = (all + 1, unconfirmed + (confirmed ? 0 : 1));
        }

        return shipments;
    }

    /// <summary>
    /// How many times the sheet counts an order: once with no shipment, else once per shipment
    /// that is not confirmed, so an order whose shipments are all confirmed counts zero times.
    /// </summary>
    public static int TimesCounted(IReadOnlyDictionary<string, (int All, int Unconfirmed)> shipments, string orderNbr)
        => shipments.TryGetValue(orderNbr, out var known) && known.All > 0 ? known.Unconfirmed : 1;

    private async Task<Dictionary<string, decimal>> ReadOpenOrdersAsync(
        string? cookie,
        Dictionary<string, (int All, int Unconfirmed)> shipments,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var excluded = new HashSet<string>(_stock.ExcludedCustomers, StringComparer.OrdinalIgnoreCase);
        var since = now.AddDays(-Math.Max(1, _stock.OpenOrderLookbackDays));
        var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var orderType in _stock.OpenOrderTypes)
        {
            var filter = $"OrderType eq '{orderType}' and {DateFilter(since)}";
            var rows = await GetArrayAsync(cookie, OrderQuery(filter), ListLimit, $"{orderType} sales orders", cancellationToken);
            AddOrderLines(rows, shipments, since, order => !excluded.Contains(Text(order, "CustomerID") ?? string.Empty), totals);
        }

        return totals;
    }

    private async Task<Dictionary<string, decimal>> ReadPromoOrdersAsync(
        string? cookie,
        Dictionary<string, (int All, int Unconfirmed)> shipments,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var since = now.AddDays(-Math.Max(1, _stock.PromoOrderLookbackDays));
        var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in _stock.PromoCustomers.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(OrFilterBatchSize))
        {
            var customers = string.Join(" or ", batch.Select(customer => $"CustomerID eq '{Escape(customer)}'"));
            var filter = $"OrderType ne '{_stock.PromoIgnoredOrderType}' and {DateFilter(since)} and ({customers})";
            var rows = await GetArrayAsync(cookie, OrderQuery(filter), ListLimit, "promotion orders", cancellationToken);
            AddOrderLines(rows, shipments, since, _ => true, totals);
        }

        return totals;
    }

    private void AddOrderLines(
        List<JsonElement> orders,
        Dictionary<string, (int All, int Unconfirmed)> shipments,
        DateTime since,
        Func<JsonElement, bool> include,
        Dictionary<string, decimal> totals)
    {
        foreach (var order in orders)
        {
            // The sheet compares the order date with getdate() less the look-back, time of day
            // included, so the day exactly at the edge does not count.
            if (LocalDate(Text(order, "Date")) is not { } date || date.ToDateTime(TimeOnly.MinValue) < since || !include(order))
            {
                continue;
            }

            var times = TimesCounted(shipments, Text(order, "OrderNbr") ?? string.Empty);
            if (times == 0)
            {
                continue;
            }

            foreach (var line in Array(order, "Details"))
            {
                var inventoryCd = Text(line, "InventoryID");
                if (!string.IsNullOrEmpty(inventoryCd) && Same(Text(line, "WarehouseID"), _stock.WarehouseId))
                {
                    totals[inventoryCd] = totals.GetValueOrDefault(inventoryCd) + times * Number(line, "OrderQty");
                }
            }
        }
    }

    /// <summary>
    /// Unreleased transfers out of the warehouse: from the sync's location, and from any location
    /// for warehouse-rule items. The sheet counts no transfers in.
    /// </summary>
    private async Task<(Dictionary<string, decimal> OutOfLocation, Dictionary<string, decimal> OutOfWarehouse)> ReadTransfersOutAsync(
        string? cookie,
        CancellationToken cancellationToken)
    {
        var rows = await GetArrayAsync(
            cookie,
            "TransferOrder?$filter=" + Uri.EscapeDataString("Status ne 'Released'") + "&$expand=Details&$select=" + TransferFields,
            ListLimit,
            "unreleased transfer orders",
            cancellationToken);

        var outOfLocation = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var outOfWarehouse = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in rows.Where(order => Same(Text(order, "FromWarehouseID"), _stock.WarehouseId)))
        {
            foreach (var line in Array(order, "Details"))
            {
                var inventoryCd = Text(line, "InventoryID");
                if (string.IsNullOrEmpty(inventoryCd))
                {
                    continue;
                }

                var quantity = Number(line, "Qty");
                outOfWarehouse[inventoryCd] = outOfWarehouse.GetValueOrDefault(inventoryCd) + quantity;
                if (Same(Text(line, "FromLocationID"), _stock.LocationId))
                {
                    outOfLocation[inventoryCd] = outOfLocation.GetValueOrDefault(inventoryCd) + quantity;
                }
            }
        }

        return (outOfLocation, outOfWarehouse);
    }

    private async Task<(decimal OnHand, string? Error)> ReadOnHandAsync(string? cookie, string inventoryCd, CancellationToken cancellationToken)
    {
        var (root, error) = await InquireAsync(cookie, inventoryCd, _stock.LocationId, cancellationToken);
        if (root is null)
        {
            return (0, error);
        }

        // A missing figure is not a zero: the item is reported as a failure and skipped.
        return RequiredNumber(root.Value, "OnHand") is { } onHand ? (onHand, null) : (0, "Acumatica returned no OnHand");
    }

    private async Task<(decimal Committed, string? Error)> ReadWarehouseCommittedAsync(string? cookie, string inventoryCd, CancellationToken cancellationToken)
    {
        var (root, error) = await InquireAsync(cookie, inventoryCd, null, cancellationToken);
        if (root is null)
        {
            return (0, error);
        }

        var booked = RequiredNumber(root.Value, "SOBooked");
        var allocated = RequiredNumber(root.Value, "SOAllocated");
        return booked is null || allocated is null
            ? (0, "Acumatica returned no warehouse SOBooked or SOAllocated")
            : (Math.Max(booked.Value, allocated.Value), null);
    }

    private async Task<(JsonElement? Root, string? Error)> InquireAsync(string? cookie, string inventoryCd, string? location, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object>
        {
            ["InventoryID"] = new { value = inventoryCd },
            ["WarehouseID"] = new { value = _stock.WarehouseId },
        };
        if (location is not null)
        {
            parameters["Location"] = new { value = location };
        }

        using var request = Request(HttpMethod.Put, EntityUri("InventoryAllocationInquiry"), cookie);
        request.Content = new StringContent(JsonSerializer.Serialize(parameters, SerializerOptions), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (null, $"HTTP {(int)response.StatusCode}");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return (document.RootElement.Clone(), null);
    }

    private async Task<List<LoadedWebOrderLine>> ReadLoadedWebOrdersAsync(
        string? cookie,
        IReadOnlyCollection<string> webOrderIds,
        CancellationToken cancellationToken)
    {
        var lines = new List<LoadedWebOrderLine>();
        foreach (var batch in webOrderIds.Distinct(StringComparer.Ordinal).Chunk(OrFilterBatchSize))
        {
            var filter = string.Join(" or ", batch.Select(id => $"CustomerOrder eq '{Escape(id)}'"));
            var rows = await GetArrayAsync(
                cookie,
                "SalesOrder?$filter=" + Uri.EscapeDataString(filter) + "&$expand=Details&$select=" + WebOrderFields,
                WebOrderListLimit,
                "sales orders for web orders",
                cancellationToken);
            foreach (var order in rows)
            {
                var webOrderId = Text(order, "CustomerOrder") ?? string.Empty;
                foreach (var line in Array(order, "Details"))
                {
                    var inventoryCd = Text(line, "InventoryID");
                    if (!string.IsNullOrEmpty(inventoryCd))
                    {
                        lines.Add(new LoadedWebOrderLine(webOrderId, inventoryCd, Number(line, "OrderQty")));
                    }
                }
            }
        }

        return lines;
    }

    private static decimal? ToBase(Dictionary<string, ItemUnits> units, string inventoryCd, string unit, decimal quantity)
    {
        if (!units.TryGetValue(inventoryCd, out var item) || string.IsNullOrEmpty(unit) || Same(unit, item.BaseUnit))
        {
            return quantity;
        }

        return item.Conversions.TryGetValue(unit, out var conversion)
            ? conversion.Multiply ? quantity * conversion.Factor : quantity / conversion.Factor
            : null;
    }

    private DateTime LocalNow()
    {
        var offset = TimeSpan.Parse(_stock.AcumaticaUtcOffset.TrimStart('+'), CultureInfo.InvariantCulture);
        if (_stock.AcumaticaUtcOffset.StartsWith('-'))
        {
            offset = -offset.Duration();
        }

        return timeProvider.GetUtcNow().ToOffset(offset).DateTime;
    }

    private string DateFilter(DateTime since)
        => $"Date ge datetimeoffset'{since:yyyy-MM-dd}T00:00:00{_stock.AcumaticaUtcOffset}'";

    private static string OrderQuery(string filter)
        => "SalesOrder?$filter=" + Uri.EscapeDataString(filter) + "&$expand=Details&$select=" + OrderFields;

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    /// <summary>The calendar date Acumatica shows, from a value such as 2026-10-30T00:00:00+02:00.</summary>
    private static DateOnly? LocalDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? DateOnly.FromDateTime(date.DateTime)
            : null;

    private async Task<List<JsonElement>> GetArrayAsync(
        string? cookie,
        string relative,
        int limit,
        string what,
        CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, EntityUri($"{relative}&$top={limit}"), cookie);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Acumatica read of {what} failed with status {(int)response.StatusCode} ({response.StatusCode}).");
        }

        // Parse from the stream: a string copy of a large list doubles the memory it needs.
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        var rows = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().Select(element => element.Clone()).ToList()
            : [];
        if (rows.Count >= limit)
        {
            throw new InvalidOperationException(
                $"Acumatica read of {what} returned {rows.Count} rows, the limit. The list may be incomplete, so the run stops.");
        }

        return rows;
    }

    /// <summary>
    /// Reads one table over the table-level OData API, following next links. This API does not use
    /// the session cookie: it takes Basic authentication as user@company.
    /// </summary>
    private async Task<List<JsonElement>> GetDacAsync(string relative, string what, CancellationToken cancellationToken)
    {
        var rows = new List<JsonElement>();
        var next = new Uri(new Uri(_acumatica.BaseUrl!.TrimEnd('/') + "/"), $"t/{Uri.EscapeDataString(_acumatica.Company!)}/api/odata/dac/{relative}");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_acumatica.Username}@{_acumatica.Company}:{_acumatica.Password}"));
        for (var page = 0; next is not null; page++)
        {
            if (page >= 100)
            {
                throw new InvalidOperationException($"Acumatica read of {what} did not end after 100 pages, so the run stops.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, next);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Acumatica read of {what} failed with status {(int)response.StatusCode} ({response.StatusCode}). "
                    + "The integration user needs read access to that table in the OData API.");
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array)
            {
                rows.AddRange(values.EnumerateArray().Select(element => element.Clone()));
            }

            next = document.RootElement.TryGetProperty("@odata.nextLink", out var link) && link.ValueKind == JsonValueKind.String
                ? new Uri(link.GetString()!, UriKind.RelativeOrAbsolute)
                : null;
            if (next is { IsAbsoluteUri: false })
            {
                next = new Uri(new Uri(_acumatica.BaseUrl!.TrimEnd('/') + "/"), next);
            }

            // The credentials go only to the Acumatica server: same scheme, host and port.
            if (next is not null
                && Uri.Compare(next, new Uri(_acumatica.BaseUrl!), UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
            {
                throw new InvalidOperationException($"Acumatica read of {what} pointed to another host, so the run stops.");
            }
        }

        return rows;
    }

    private async Task<string?> SignInAsync(CancellationToken cancellationToken)
    {
        var login = new Dictionary<string, string>
        {
            ["name"] = _acumatica.Username!,
            ["password"] = _acumatica.Password!,
        };
        if (!string.IsNullOrWhiteSpace(_acumatica.Company))
        {
            login["company"] = _acumatica.Company;
        }

        using var response = await httpClient.PostAsJsonAsync(Root("entity/auth/login"), login, SerializerOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Acumatica sign-in failed with status {(int)response.StatusCode} ({response.StatusCode}).");
        }

        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return null;
        }

        var baseUri = new Uri(_acumatica.BaseUrl!.TrimEnd('/') + "/");
        var jar = new CookieContainer();
        foreach (var setCookie in setCookies)
        {
            jar.SetCookies(baseUri, setCookie);
        }

        var header = jar.GetCookieHeader(baseUri);
        return string.IsNullOrWhiteSpace(header) ? null : header;
    }

    private async Task SignOutAsync(string? cookie)
    {
        // Licences cap concurrent API sessions, so the session is always released.
        using var request = Request(HttpMethod.Post, Root("entity/auth/logout"), cookie);
        request.Content = new StringContent(string.Empty);
        using var response = await httpClient.SendAsync(request, CancellationToken.None);
    }

    private Uri EntityUri(string relative)
        => Root($"entity/{_acumatica.EndpointName}/{_acumatica.EndpointVersion}/{relative}");

    private Uri Root(string relative) => new(new Uri(_acumatica.BaseUrl!.TrimEnd('/') + "/"), relative);

    private static HttpRequestMessage Request(HttpMethod method, Uri uri, string? cookie)
    {
        var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        return request;
    }

    private static bool Same(string? left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<JsonElement> Array(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var field)
            && field.ValueKind == JsonValueKind.Object
            && field.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    /// <summary>A field of the table-level OData API: a plain value, not a {"value": ...} object.</summary>
    private static string? DacText(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    // For document lines: an absent quantity on a line adds nothing.
    private static decimal Number(JsonElement element, string name) => RequiredNumber(element, name) ?? 0m;

    private static decimal? RequiredNumber(JsonElement element, string name)
        => element.TryGetProperty(name, out var field)
            && field.ValueKind == JsonValueKind.Object
            && field.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal()
            : null;

    private sealed record ItemUnits(string BaseUnit, Dictionary<string, (bool Multiply, decimal Factor)> Conversions);
}
