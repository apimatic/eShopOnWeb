using System;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class ShopperName
{
    public static (string FirstName, string LastName) FromUser(string userName, string? email)
    {
        var source = !string.IsNullOrWhiteSpace(email) ? email : userName;
        var local = source.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Sanitize(parts[0], "Shopper") : "Shopper";
        var last = parts.Length > 1 ? Sanitize(parts[1], "eShopOnWeb") : "eShopOnWeb";
        return (first, last);
    }

    private static string Sanitize(string value, string fallback)
    {
        var cleaned = new string(value.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return fallback;
        }

        return char.ToUpperInvariant(cleaned[0]) + cleaned[1..].ToLowerInvariant();
    }
}
