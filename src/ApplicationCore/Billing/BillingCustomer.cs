using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record BillingCustomer(string Reference, string Email, string FirstName, string LastName)
{
    public static BillingCustomer FromUser(string id, string? email, string? userName)
    {
        var address = FirstNonEmpty(email, userName, $"{id}@users.eshop.local");
        var separator = address.IndexOf('@');
        var local = separator > 0 ? address[..separator] : address;
        var parts = local.Split(new[] { '.', '+', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? parts[0] : "Shopper";
        var last = parts.Length > 1 ? parts[1] : "User";
        return new BillingCustomer(id, address, first, last);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!;
            }
        }

        return "shopper@users.eshop.local";
    }
}
