using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class AuthenticatedShopperResolver
{
    internal static async Task<ShopperBillingIdentity?> ResolveAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        var email = user?.Email ?? user?.UserName;
        if (user is null || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var localPart = email.Split('@', 2)[0];
        var nameParts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = ToDisplayName(nameParts.FirstOrDefault() ?? "Shopper");
        var lastName = ToDisplayName(nameParts.Skip(1).LastOrDefault() ?? "Customer");
        return new ShopperBillingIdentity(user.Id, email, firstName, lastName);
    }

    private static string ToDisplayName(string value) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
}
