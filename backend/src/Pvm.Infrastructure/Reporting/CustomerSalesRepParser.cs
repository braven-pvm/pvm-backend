using System.Text.Json;
using Pvm.Application.Reporting;

namespace Pvm.Infrastructure.Reporting;

/// <summary>
/// Reads one page of the contract API entity Customer with its Salespersons detail. The
/// salesperson marked Default is the current rep. A customer with no default has no rep.
/// </summary>
public static class CustomerSalesRepParser
{
    public static IReadOnlyList<CustomerSalesRep> ParsePage(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new SellInReadException("The customer list response is not a list.");
        }

        var customers = new List<CustomerSalesRep>(document.RootElement.GetArrayLength());
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var customerId = Value(item, "CustomerID")
                ?? throw new SellInReadException("A customer in the customer list has no CustomerID.");

            JsonElement? current = null;
            if (item.TryGetProperty("Salespersons", out var salespersons) && salespersons.ValueKind == JsonValueKind.Array)
            {
                foreach (var salesperson in salespersons.EnumerateArray())
                {
                    if (salesperson.TryGetProperty("Default", out var isDefault)
                        && isDefault.TryGetProperty("value", out var flag)
                        && flag.ValueKind == JsonValueKind.True)
                    {
                        current = salesperson;
                        break;
                    }
                }
            }

            customers.Add(new CustomerSalesRep(
                customerId,
                current is { } rep ? Value(rep, "SalespersonID") : null,
                current is { } named ? Value(named, "Name") : null));
        }

        return customers;
    }

    private static string? Value(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var field)
            || field.ValueKind != JsonValueKind.Object
            || !field.TryGetProperty("value", out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
