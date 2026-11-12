using System.Globalization;
using System.Text.Json;
using Pvm.Application.Reporting;
using Pvm.Domain.Reporting;

namespace Pvm.Infrastructure.Reporting;

/// <summary>
/// Reads the OData response of the inquiry PVM-Sales-Lines. A row that is not a sales line
/// (an unknown document type, no customer or no item) is skipped and counted. A value that
/// cannot be read fails the whole response, because a sales total with a missing line is wrong
/// without a sign of it.
/// </summary>
public static class SellInRowParser
{
    // Acumatica OData gives local times without an offset. PVM's instance runs in South Africa,
    // which has no daylight saving time.
    private static readonly TimeSpan InstanceOffset = TimeSpan.FromHours(2);

    public static (IReadOnlyList<SellInRow> Rows, int Skipped) Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("odata.nextLink", out _) || root.TryGetProperty("@odata.nextLink", out _))
        {
            throw new SellInReadException(
                "The inquiry returned a partial page with a next link. The load reads whole periods only.");
        }

        if (!root.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            throw new SellInReadException("The inquiry response has no 'value' array.");
        }

        var rows = new List<SellInRow>(values.GetArrayLength());
        var skipped = 0;
        var index = 0;
        foreach (var item in values.EnumerateArray())
        {
            index++;
            try
            {
                var row = ParseRow(item);
                if (row is null)
                {
                    skipped++;
                }
                else
                {
                    rows.Add(row);
                }
            }
            catch (Exception exception) when (exception is FormatException or InvalidOperationException or OverflowException)
            {
                throw new SellInReadException($"Row {index} of the inquiry response cannot be read: {exception.Message}");
            }
        }

        return (rows, skipped);
    }

    private static SellInRow? ParseRow(JsonElement item)
    {
        var type = SellInDocumentTypes.FromAcumaticaCode(Text(item, "TranType"));
        var customerId = Text(item, "CustomerID");
        var inventoryId = Text(item, "InventoryID");
        var reference = Text(item, "RefNbr");
        if (type is null || customerId is null || inventoryId is null || reference is null)
        {
            return null;
        }

        return new SellInRow(
            type.Value,
            reference,
            Integer(item, "LineNbr"),
            DateOnly.FromDateTime(Timestamp(item, "TranDate")?.DateTime
                ?? throw new FormatException("TranDate is empty.")),
            Text(item, "TranPeriodID"),
            customerId,
            Text(item, "CustomerName") ?? customerId,
            Text(item, "CustomerClassID"),
            Text(item, "CustomerClassName"),
            Text(item, "Province"),
            Text(item, "SalesRepID"),
            Text(item, "SalesRepName"),
            inventoryId,
            Text(item, "ItemDescription") ?? inventoryId,
            Text(item, "ItemClassID"),
            Text(item, "ItemClassName"),
            Text(item, "WarehouseID"),
            Text(item, "WarehouseName"),
            Text(item, "LocationID"),
            Text(item, "LocationName"),
            Number(item, "Quantity"),
            Text(item, "UOM"),
            Number(item, "BaseQuantity"),
            Number(item, "Amount"),
            Timestamp(item, "LastModified"));
    }

    private static string? Text(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static int Integer(JsonElement item, string name) =>
        int.Parse(Text(item, name) ?? throw new FormatException($"{name} is empty."), NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static decimal Number(JsonElement item, string name)
    {
        var text = Text(item, name);
        return text is null ? 0m : decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? Timestamp(JsonElement item, string name)
    {
        var text = Text(item, name);
        if (text is null)
        {
            return null;
        }

        if (text.StartsWith("/Date(", StringComparison.Ordinal))
        {
            // The OData verbose form: /Date(milliseconds)/ in UTC.
            var milliseconds = long.Parse(text[6..text.IndexOf(')', StringComparison.Ordinal)].Split('+', '-')[0], CultureInfo.InvariantCulture);
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToOffset(InstanceOffset);
        }

        var parsed = DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return parsed.Kind switch
        {
            DateTimeKind.Unspecified => new DateTimeOffset(parsed, InstanceOffset),
            _ => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture).ToOffset(InstanceOffset)
        };
    }
}

/// <summary>The inquiry gave a response that the load cannot use. The run fails.</summary>
public sealed class SellInReadException(string message) : Exception(message);
