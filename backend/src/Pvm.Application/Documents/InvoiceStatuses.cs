namespace Pvm.Application.Documents;

/// <summary>Invoice statuses that are never a document's target (design 6.5): voided and cancelled.</summary>
public static class InvoiceStatuses
{
    private static readonly HashSet<string> OutOfScope =
        new(["Voided", "Canceled", "Cancelled"], StringComparer.OrdinalIgnoreCase);

    /// <summary>True for a voided or cancelled status, ignoring case and surrounding spaces.</summary>
    public static bool IsOutOfScope(string? status)
        => status is not null && OutOfScope.Contains(status.Trim());
}
