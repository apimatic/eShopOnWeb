using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public static class ShopperNameFormatter
{
    public static (string FirstName, string LastName) Split(Shopper shopper)
    {
        var source = !string.IsNullOrWhiteSpace(shopper.UserName) ? shopper.UserName : shopper.Email;
        var local = source.Contains('@', StringComparison.Ordinal)
            ? source[..source.IndexOf('@')]
            : source;
        if (string.IsNullOrWhiteSpace(local))
        {
            local = "Shopper";
        }

        return (Truncate(local, 40), "eShopOnWeb");
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
