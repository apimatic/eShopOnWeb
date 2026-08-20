using System;
using System.Linq;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

internal static class ShopperName
{
    public static (string FirstName, string LastName) FromIdentity(string email, string userName)
    {
        var local = LocalPart(email) ?? LocalPart(userName) ?? "Shopper";
        var first = Capitalize(Sanitize(local));
        return (first, "Customer");
    }

    private static string? LocalPart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var at = value.IndexOf('@');
        return at > 0 ? value[..at] : value;
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.Length == 0 ? "Shopper" : builder.ToString();
    }

    private static string Capitalize(string value)
    {
        if (value.Length == 1)
        {
            return value.ToUpperInvariant();
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
