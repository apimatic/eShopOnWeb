using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentityResolver
{
    public static async Task<ShopperIdentity?> ResolveAsync(
        ClaimsPrincipal? principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = user.Email ?? user.UserName ?? userName;
        var localPart = (email.Split('@')[0] ?? "shopper").Replace('.', ' ').Replace('_', ' ').Trim();
        if (string.IsNullOrWhiteSpace(localPart))
        {
            localPart = "Shopper";
        }

        var parts = localPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstName = Capitalize(parts[0]);
        var lastName = parts.Length > 1 ? Capitalize(string.Join(' ', parts.Skip(1))) : "eShopOnWeb";

        return new ShopperIdentity
        {
            UserId = user.Id,
            Email = email,
            FirstName = firstName,
            LastName = lastName
        };
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Shopper";
        }

        return char.ToUpperInvariant(value[0]) + (value.Length > 1 ? value[1..] : string.Empty);
    }
}
