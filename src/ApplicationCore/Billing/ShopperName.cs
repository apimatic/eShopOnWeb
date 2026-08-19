using System;
using System.Globalization;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public static class ShopperName
{
    public static (string FirstName, string LastName) FromEmail(string? email, string? userName)
    {
        var source = !string.IsNullOrWhiteSpace(email) ? email : userName;
        if (string.IsNullOrWhiteSpace(source))
        {
            return ("Shopper", "eShopOnWeb");
        }

        var at = source.IndexOf('@');
        if (at <= 0)
        {
            return (TitleCase(source), "eShopOnWeb");
        }

        var local = source[..at];
        var domain = source[(at + 1)..];
        var domainLabel = domain.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "eShopOnWeb";
        return (TitleCase(local), TitleCase(domainLabel));
    }

    private static string TitleCase(string value)
    {
        var cleaned = new string(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return "Shopper";
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cleaned.ToLowerInvariant());
    }
}
