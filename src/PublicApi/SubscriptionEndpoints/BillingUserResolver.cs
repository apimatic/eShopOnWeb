using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingUserResolver
{
    public static async Task<BillingUser?> ResolveAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? userName : user.Email;
        var localPart = email.Split('@', 2, StringSplitOptions.TrimEntries)[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
        return new BillingUser(user.Id, email, firstName, "Customer");
    }
}
