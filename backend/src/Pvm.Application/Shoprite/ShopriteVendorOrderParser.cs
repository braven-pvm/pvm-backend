using System.Globalization;
using System.Text.Json;

namespace Pvm.Application.Shoprite;

public static class ShopriteVendorOrderParser
{
    public static ShopritePurchaseOrderBatch Parse(string rawPayload)
    {
        using var document = JsonDocument.Parse(rawPayload);
        var root = document.RootElement;

        if (!root.TryGetProperty("orderField", out var orderField))
        {
            return new ShopritePurchaseOrderBatch(rawPayload, []);
        }

        var orders = EnumerateArrayOrSingle(orderField)
            .Select(ParseOrder)
            .Where(order => !string.IsNullOrWhiteSpace(order.PurchaseOrderNumber))
            .ToArray();

        return new ShopritePurchaseOrderBatch(rawPayload, orders);
    }

    private static ShopritePurchaseOrder ParseOrder(JsonElement order)
    {
        var buyer = TryGetProperty(order, "buyerField");
        var seller = TryGetProperty(order, "sellerField");
        var shipTo = TryGetProperty(order, "shipToField");
        var inventoryLocation = TryGetProperty(order, "inventoryLocationField");
        var delivery = ResolveDeliveryLocation(shipTo, buyer, inventoryLocation);

        return new ShopritePurchaseOrder(
            PurchaseOrderNumber: GetString(order, "orderIdentificationField", "entityIdentificationField") ?? string.Empty,
            OrderHeaderId: GetAdditionalPartyIdentification(
                TryGetProperty(order, "orderIdentificationField"),
                "BUYER_ASSIGNED_ORDER_HEADERID"),
            OrderTypeCode: GetString(order, "orderTypeCodeField", "valueField"),
            OrderTypeLabel: GetString(order, "orderTypeCodeField", "codeListVersionField"),
            SupplierGln: GetString(seller, "glnField"),
            BuyerGln: GetString(buyer, "glnField"),
            DeliveryGln: delivery.Gln,
            DeliveryLocationCode: delivery.Code,
            DeliveryLocationName: delivery.Name,
            DeliveryLocationSource: delivery.Source,
            CurrencyCode: GetString(order, "totalMonetaryAmountIncludingTaxesField", "currencyCodeField")
                ?? GetString(order, "totalMonetaryAmountExcludingTaxesField", "currencyCodeField"),
            TotalExcludingTax: GetDecimal(order, "totalMonetaryAmountExcludingTaxesField", "valueField"),
            TotalIncludingTax: GetDecimal(order, "totalMonetaryAmountIncludingTaxesField", "valueField"),
            TotalTax: GetDecimal(order, "totalTaxAmountField", "valueField"),
            CreatedAt: GetDateTimeOffset(order, "creationDateTimeField"),
            LastUpdatedAt: GetDateTimeOffset(order, "lastUpdateDateTimeField"),
            RawOrderJson: order.GetRawText(),
            Lines: ParseLines(order).ToArray());
    }

    private static IEnumerable<ShopritePurchaseOrderLine> ParseLines(JsonElement order)
    {
        var lineField = TryGetProperty(order, "orderLineItemField");
        if (lineField is null)
        {
            yield break;
        }

        foreach (var line in EnumerateArrayOrSingle(lineField.Value))
        {
            var tradeItem = TryGetProperty(line, "transactionalTradeItemField");

            yield return new ShopritePurchaseOrderLine(
                LineNumber: (int?)GetDecimal(line, "lineItemNumberField") ?? 0,
                Gtin: GetString(tradeItem, "gtinField"),
                BuyerItemId: GetAdditionalTradeItemIdentification(tradeItem, "BUYER_ASSIGNED_ITEMID"),
                BuyerItemDescription: GetAdditionalTradeItemIdentification(tradeItem, "BUYER_ASSIGNED_ITEM_DESCRIPTION"),
                SupplierItemId: GetAdditionalTradeItemIdentification(tradeItem, "SUPPLIER_ASSIGNED_ITEMID"),
                Description: GetString(tradeItem, "tradeItemDescriptionField", "valueField"),
                RequestedQuantity: GetDecimal(line, "requestedQuantityField", "valueField"),
                MeasurementUnitCode: GetString(line, "requestedQuantityField", "measurementUnitCodeField"),
                NetAmount: GetDecimal(line, "netAmountField", "valueField"),
                NetPrice: GetDecimal(line, "netPriceField", "valueField"),
                MonetaryAmountExcludingTaxes: GetDecimal(line, "monetaryAmountExcludingTaxesField", "valueField"),
                MonetaryAmountIncludingTaxes: GetDecimal(line, "monetaryAmountIncludingTaxesField", "valueField"));
        }
    }

    private static DeliveryLocation ResolveDeliveryLocation(
        JsonElement? shipTo,
        JsonElement? buyer,
        JsonElement? inventoryLocation)
    {
        var fromShipTo = ResolvePartyLocation(shipTo, "shipTo");
        if (!string.IsNullOrWhiteSpace(fromShipTo.Gln))
        {
            return fromShipTo;
        }

        var fromBuyer = ResolvePartyLocation(buyer, "buyer");
        if (!string.IsNullOrWhiteSpace(fromBuyer.Gln))
        {
            return fromBuyer;
        }

        var fromInventory = ResolvePartyLocation(inventoryLocation, "inventoryLocation");
        if (!string.IsNullOrWhiteSpace(fromInventory.Gln))
        {
            return fromInventory;
        }

        return new DeliveryLocation(null, null, null, "unresolved");
    }

    private static DeliveryLocation ResolvePartyLocation(JsonElement? party, string source)
        => new(
            Gln: GetString(party, "glnField"),
            Code: GetAdditionalPartyIdentification(party, "BUYER_ASSIGNED_IDENTIFIER_FOR_A_PARTY"),
            Name: GetAdditionalPartyIdentification(party, "BUYER_ASSIGNED_DESCRIPTION_FOR_A_PARTY"),
            Source: source);

    private static string? GetAdditionalPartyIdentification(JsonElement? element, string typeCode)
    {
        var partyIdentifications = TryGetProperty(element, "additionalPartyIdentificationField");
        if (partyIdentifications is null)
        {
            var contentOwner = TryGetProperty(element, "contentOwnerField");
            partyIdentifications = TryGetProperty(contentOwner, "additionalPartyIdentificationField");
        }

        return partyIdentifications is null
            ? null
            : FindTypedValue(partyIdentifications.Value, "additionalPartyIdentificationTypeCodeField", typeCode);
    }

    private static string? GetAdditionalTradeItemIdentification(JsonElement? element, string typeCode)
    {
        var identifications = TryGetProperty(element, "additionalTradeItemIdentificationField");
        return identifications is null
            ? null
            : FindTypedValue(identifications.Value, "additionalTradeItemIdentificationTypeCodeField", typeCode);
    }

    private static string? FindTypedValue(JsonElement element, string typePropertyName, string typeCode)
    {
        foreach (var identification in EnumerateArrayOrSingle(element))
        {
            if (string.Equals(GetString(identification, typePropertyName), typeCode, StringComparison.OrdinalIgnoreCase))
            {
                return GetString(identification, "valueField");
            }
        }

        return null;
    }

    private static IEnumerable<JsonElement> EnumerateArrayOrSingle(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                yield return item;
            }
        }
        else if (element.ValueKind != JsonValueKind.Null && element.ValueKind != JsonValueKind.Undefined)
        {
            yield return element;
        }
    }

    private static JsonElement? TryGetProperty(JsonElement? element, string propertyName)
    {
        if (element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return element.Value.TryGetProperty(propertyName, out var property)
            ? property
            : null;
    }

    private static string? GetString(JsonElement? element, params string[] path)
    {
        var current = element;
        foreach (var propertyName in path)
        {
            current = TryGetProperty(current, propertyName);
            if (current is null)
            {
                return null;
            }
        }

        if (current is not { } jsonValue)
        {
            return null;
        }

        return jsonValue.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(jsonValue.GetString())
                ? null
                : jsonValue.GetString(),
            JsonValueKind.Number => jsonValue.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static decimal? GetDecimal(JsonElement? element, params string[] path)
    {
        var value = GetString(element, path);
        if (value is null)
        {
            return null;
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement? element, params string[] path)
    {
        var value = GetString(element, path);
        if (value is null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private sealed record DeliveryLocation(string? Gln, string? Code, string? Name, string Source);
}
