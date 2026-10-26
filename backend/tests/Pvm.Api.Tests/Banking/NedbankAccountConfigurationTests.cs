using System.Text.Json;

namespace Pvm.Api.Tests.Banking;

/// <summary>
/// Guards the shipped Nedbank account map. A typo here is not a crash: it quietly posts one
/// branch's statement into another branch's cash account, which is expensive to unpick and has
/// happened once already by hand.
/// </summary>
public sealed class NedbankAccountConfigurationTests
{
    private static readonly Dictionary<string, string> Accounts = ReadConfiguredAccounts();

    [Theory]
    [InlineData("1644294346", "PVMNEDBANK")]
    [InlineData("1497204518", "WCNEDBANK")]
    [InlineData("1033436658", "ECNEDBANK")]
    [InlineData("1497176344", "KZNNEDBANK")]
    [InlineData("1497199883", "FSNEDBANK")]
    public void Each_bank_account_maps_to_its_own_cash_account(string accountNumber, string cashAccount)
    {
        Assert.True(
            Accounts.ContainsKey(accountNumber),
            $"Bank account {accountNumber} is not configured. Statements for it would be refused.");
        Assert.Equal(cashAccount, Accounts[accountNumber]);
    }

    [Fact]
    public void Account_numbers_are_digits_only()
    {
        // The lookup compares against the ACCTID in the file. A stray space or letter would
        // never match, and the statement would be refused with a confusing message.
        foreach (var number in Accounts.Keys)
        {
            Assert.True(
                number.Length > 0 && number.All(char.IsDigit),
                $"Account number '{number}' is not digits only.");
        }
    }

    [Fact]
    public void No_two_bank_accounts_share_a_cash_account()
    {
        // Two numbers pointing at one cash account is almost always a copy-paste slip, and it
        // merges two branches' money into a single reconciliation.
        var duplicates = Accounts
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} <- {string.Join(", ", group.Select(pair => pair.Key))}")
            .ToList();

        Assert.True(duplicates.Count == 0, "Cash account reused: " + string.Join("; ", duplicates));
    }

    private static Dictionary<string, string> ReadConfiguredAccounts()
    {
        var path = FindAppSettings()
            ?? throw new InvalidOperationException("Could not locate Pvm.Api/appsettings.json.");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var accounts = document.RootElement.GetProperty("Nedbank").GetProperty("Accounts");

        return accounts.EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.GetString() ?? string.Empty,
                StringComparer.Ordinal);
    }

    // Walk up from the test assembly rather than relying on the file being copied to the output.
    private static string? FindAppSettings()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "backend", "src", "Pvm.Api", "appsettings.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
