using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingUserResolver
{
    public static async Task<BillingUser?> ResolveAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var applicationUser = await userManager.FindByNameAsync(username);
        if (applicationUser is null)
        {
            return null;
        }

        var email = applicationUser.Email ?? applicationUser.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var displayName = email.Split('@', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = "Customer";
        }

        return new BillingUser(applicationUser.Id, email, displayName, "eShopOnWeb");
    }
}
