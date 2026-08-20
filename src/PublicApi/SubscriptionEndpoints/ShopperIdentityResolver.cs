using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentityResolver
{
    public static async Task<ShopperIdentity?> ResolveAsync(
        UserManager<ApplicationUser> userManager,
        ClaimsPrincipal principal)
    {
        ApplicationUser? user = null;

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            user = await userManager.FindByIdAsync(userId);
        }

        if (user is null)
        {
            var userName = principal.Identity?.Name;
            if (!string.IsNullOrWhiteSpace(userName))
            {
                user = await userManager.FindByNameAsync(userName);
            }
        }

        if (user is null)
        {
            return null;
        }

        var email = user.Email ?? user.UserName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(user.Id))
        {
            return null;
        }

        var (firstName, lastName) = NamesFrom(user);
        return new ShopperIdentity
        {
            UserId = user.Id,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
        };
    }

    private static (string FirstName, string LastName) NamesFrom(ApplicationUser user)
    {
        var source = user.UserName ?? user.Email ?? "shopper";
        var local = source.Split('@')[0];
        var token = new string(local.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(token))
        {
            token = "Shopper";
        }

        var first = char.ToUpperInvariant(token[0]) + token[1..];
        return (first, "eShopOnWeb");
    }
}
