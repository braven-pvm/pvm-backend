using System.Text;

namespace Pvm.Application.Documents;

/// <summary>
/// Decides whether a client name read from a paper agrees with an invoice's customer, parent
/// customer, or ship-to name. Case, punctuation and common company suffixes are ignored; two
/// names agree when the words of one appear, in order and together, among the words of the other.
/// Whole words only: "OK Foods" does not agree with "Bokomo Foods".
/// </summary>
public static class CompanyNameComparer
{
    private static readonly HashSet<string> Suffixes = ["pty", "ltd", "limited", "cc", "inc"];

    /// <summary>True when the name agrees with the customer, parent customer or ship-to name.</summary>
    public static bool Agrees(string extracted, InvoiceCandidate invoice)
        => AgreesWithAny(extracted, invoice.CustomerName, invoice.ParentCustomerName, invoice.ShipToName);

    /// <summary>
    /// True when the name agrees with the customer or ship-to name. The parent customer is left
    /// out: it names a whole chain, so it does not point to one invoice's customer.
    /// </summary>
    public static bool AgreesDirectly(string extracted, InvoiceCandidate invoice)
        => AgreesWithAny(extracted, invoice.CustomerName, invoice.ShipToName);

    private static bool AgreesWithAny(string extracted, params string?[] names)
    {
        var name = Words(extracted);
        if (name.Length == 0)
        {
            return false;
        }

        return names
            .Select(Words)
            .Where(other => other.Length > 0)
            .Any(other => ContainsRun(name, other) || ContainsRun(other, name));
    }

    private static bool ContainsRun(string[] words, string[] run)
    {
        for (var start = 0; start + run.Length <= words.Length; start++)
        {
            if (words.AsSpan(start, run.Length).SequenceEqual(run))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] Words(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        var lower = name.ToLowerInvariant();
        var spaced = new StringBuilder(lower.Length);
        foreach (var c in lower)
        {
            spaced.Append(char.IsLetterOrDigit(c) || c == '/' ? c : ' ');
        }

        // "t/a" is removed as a word before the slash goes, so it cannot become "ta".
        return spaced.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word != "t/a")
            .Select(word => word.Replace("/", string.Empty, StringComparison.Ordinal))
            .Where(word => word.Length > 0 && !Suffixes.Contains(word))
            .ToArray();
    }
}
