using System;
using System.Globalization;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Maxio rejects a customer whose first or last name is blank, but eShopOnWeb identities
/// only carry an email address. This derives a deterministic, non-blank pair of names from
/// the user name whenever the caller does not supply real ones.
/// </summary>
public static class BillingCustomerNaming
{
    private const string FallbackFirstName = "eShop";
    private const string FallbackLastName = "Customer";

    private static readonly char[] Separators = { '.', '_', '-', '+' };

    public static (string FirstName, string LastName) Derive(string userName, string? firstName, string? lastName)
    {
        var first = Clean(firstName);
        var last = Clean(lastName);
        if (first is not null && last is not null)
        {
            return (first, last);
        }

        var localPart = (userName ?? string.Empty).Split('@')[0];
        var words = localPart
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(Titleize)
            .Where(w => w.Length > 0)
            .ToArray();

        first ??= words.Length > 0 ? words[0] : FallbackFirstName;
        last ??= words.Length > 1 ? string.Join(" ", words.Skip(1)) : FallbackLastName;

        return (first, last);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Titleize(string word) =>
        word.Length == 0
            ? word
            : char.ToUpper(word[0], CultureInfo.InvariantCulture) + word.Substring(1);
}
