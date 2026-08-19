using System;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public static class ShopperNameFormatter
{
    public static (string FirstName, string LastName) FromIdentity(ShopperIdentity shopper)
    {
        ArgumentNullException.ThrowIfNull(shopper);

        var source = !string.IsNullOrWhiteSpace(shopper.UserName) ? shopper.UserName : shopper.Email;
        if (string.IsNullOrWhiteSpace(source))
        {
            return ("Shopper", "User");
        }

        var local = source.Contains('@') ? source.Split('@')[0] : source;
        var parts = local
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return ("Shopper", "User");
        }

        if (parts.Length == 1)
        {
            return (Capitalize(parts[0]), "Shopper");
        }

        return (Capitalize(parts[0]), Capitalize(string.Join(' ', parts.Skip(1))));
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (value.Length == 1)
        {
            return value.ToUpperInvariant();
        }

        return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }
}
