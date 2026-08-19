using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi;

internal static class ShopperIdentityFactory
{
    public static async Task<ShopperIdentity?> FromHttpContextAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrWhiteSpace(user.Id))
        {
            return null;
        }

        var email = user.Email ?? user.UserName ?? $"{user.Id}@eshop.local";
        var (firstName, lastName) = SplitDisplayName(email);
        return new ShopperIdentity(user.Id, email, firstName, lastName);
    }

    private static (string FirstName, string LastName) SplitDisplayName(string email)
    {
        var local = email.Split('@')[0];
        var parts = local.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Capitalize(parts[1]) : "eShopOnWeb";
        return (first, last);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
