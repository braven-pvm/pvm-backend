using System.Text;

namespace Pvm.Application.Documents;

/// <summary>
/// Decides whether a client name read from a paper agrees with an invoice's customer, parent
/// customer, or ship-to name. Case, punctuation, spacing and common company suffixes are ignored;
/// two names agree when one contains the other.
/// </summary>
public static class CompanyNameComparer
{
    private static readonly HashSet<string> Suffixes = ["pty", "ltd", "limited", "cc", "inc"];

    public static bool Agrees(string extracted, InvoiceCandidate invoice)
    {
        var name = Normalise(extracted);
        if (name.Length == 0)
        {
            return false;
        }

        return new[] { invoice.CustomerName, invoice.ParentCustomerName, invoice.ShipToName }
            .Select(Normalise)
            .Where(other => other.Length > 0)
            .Any(other => name.Contains(other, StringComparison.Ordinal) || other.Contains(name, StringComparison.Ordinal));
    }

    private static string Normalise(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var lower = name.ToLowerInvariant();
        var spaced = new StringBuilder(lower.Length);
        foreach (var c in lower)
        {
            spaced.Append(char.IsLetterOrDigit(c) || c == '/' ? c : ' ');
        }

        // "t/a" is removed as a word before the slash goes, so it cannot become "ta".
        var words = spaced.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word != "t/a")
            .Select(word => word.Replace("/", string.Empty, StringComparison.Ordinal))
            .Where(word => word.Length > 0 && !Suffixes.Contains(word));

        // Joined without spaces, so "Shoprite-Checkers" and "Shoprite Checkers" agree.
        return string.Concat(words);
    }
}
