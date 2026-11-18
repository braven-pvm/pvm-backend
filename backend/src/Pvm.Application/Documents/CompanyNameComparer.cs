using System.Text;

namespace Pvm.Application.Documents;

/// <summary>
/// Decides whether a client name read from a paper agrees with an invoice's customer, parent
/// customer, or ship-to name. Case, punctuation, common company suffixes and generic words such as
/// "pharmacies" or "holdings" are ignored. Two names agree when every word of the shorter one
/// appears in the longer one in the same order; other words may come in between, so
/// "Dis-Chem Mondeor S007" agrees with "DIS-CHEM PHARMACIES LTD MONDEOR S007". A one-word name
/// must appear as a whole word: "Shoprite" agrees with "Shoprite Checkers". Whole words only:
/// "OK Foods" does not agree with "Bokomo Foods".
/// </summary>
public static class CompanyNameComparer
{
    private static readonly HashSet<string> Suffixes = ["pty", "ltd", "limited", "cc", "inc"];

    /// <summary>Words that say what kind of business it is, not which one; left out unless nothing else is left.</summary>
    private static readonly HashSet<string> GenericWords =
        ["pharmacies", "pharmacy", "stores", "store", "group", "holdings", "trading", "the", "and"];

    /// <summary>The fewest words a name needs before other words may come between them.</summary>
    private const int MinWordsForGaps = 2;

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
            .Any(other => name.Length <= other.Length ? Within(name, other) : Within(other, name));
    }

    /// <summary>True when the shorter name's words appear in the longer name's words.</summary>
    private static bool Within(string[] shorter, string[] longer)
        => shorter.Length >= MinWordsForGaps
            ? InOrder(longer, shorter)
            : ContainsRun(longer, shorter);

    /// <summary>True when every word of <paramref name="wanted"/> appears in <paramref name="words"/>, in the same order, with any words between.</summary>
    private static bool InOrder(string[] words, string[] wanted)
    {
        var next = 0;
        foreach (var word in words)
        {
            if (next < wanted.Length && word == wanted[next])
            {
                next++;
            }
        }

        return next == wanted.Length;
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
        var words = spaced.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word != "t/a")
            .Select(word => word.Replace("/", string.Empty, StringComparison.Ordinal))
            .Where(word => word.Length > 0 && !Suffixes.Contains(word))
            .ToArray();

        var specific = words.Where(word => !GenericWords.Contains(word)).ToArray();
        return specific.Length > 0 ? specific : words;
    }
}
