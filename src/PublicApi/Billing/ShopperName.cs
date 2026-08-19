using System;
using System.Linq;

namespace Microsoft.eShopWeb.PublicApi.Billing;

internal static class ShopperName
{
    public static (string FirstName, string LastName) FromIdentity(string? userName, string? email)
    {
        var source = !string.IsNullOrWhiteSpace(email) ? email : userName;
        if (string.IsNullOrWhiteSpace(source))
        {
            return ("Shopper", "eShopOnWeb");
        }

        var local = source.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '+', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Capitalize(parts[^1]) : "eShopOnWeb";
        if (string.Equals(first, last, StringComparison.OrdinalIgnoreCase))
        {
            last = "eShopOnWeb";
        }

        return (first, last);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (value.Length == 1)
        {
            return value.ToUpperInvariant();
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
