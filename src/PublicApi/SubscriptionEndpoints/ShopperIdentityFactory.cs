using System;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentityFactory
{
    public static ShopperIdentity FromUser(ApplicationUser user)
    {
        var email = user.Email ?? user.UserName ?? $"{user.Id}@eshop.local";
        var local = email.Split('@')[0];
        var parts = local.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Capitalize(string.Join(' ', parts.Skip(1))) : "eShopOnWeb";
        return new ShopperIdentity(user.Id, email, first, last);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        return char.ToUpperInvariant(value[0]) + (value.Length > 1 ? value[1..] : string.Empty);
    }
}
