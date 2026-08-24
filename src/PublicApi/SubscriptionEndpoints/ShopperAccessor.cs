using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the authenticated JWT principal to the shopper identity used for billing.
/// </summary>
public static class ShopperAccessor
{
    public static async Task<ShopperIdentity?> FromPrincipalAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var username = principal.Identity?.Name
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("unique_name")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(username))
            return null;

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
            return null;

        var email = user.Email ?? username;
        var localPart = email.Split('@')[0];
        var nameParts = localPart.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : localPart;
        var lastName = nameParts.Length > 1 ? nameParts[1] : "Shopper";

        return new ShopperIdentity(user.Id, email, firstName, lastName);
    }
}
