using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingUserResolver
{
    public static async Task<BillingUser> ResolveAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = httpContext.User.Identity?.Name;
        ApplicationUser? applicationUser = null;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            applicationUser = await userManager.FindByIdAsync(userId);
        }

        if (applicationUser is null && !string.IsNullOrWhiteSpace(userName))
        {
            applicationUser = await userManager.FindByNameAsync(userName);
        }

        if (applicationUser is null)
        {
            throw new BillingRequestException("The authenticated user no longer exists.", 401);
        }

        var email = applicationUser.Email ?? applicationUser.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new BillingRequestException("The authenticated account requires an email address.", 422);
        }

        var localPart = email.Split('@', 2)[0];
        var nameParts = localPart.Split(
            new[] { '.', '_', '-', ' ' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstName = nameParts.FirstOrDefault() ?? "eShop";
        var lastName = nameParts.Length > 1 ? nameParts[^1] : "Customer";

        return new BillingUser(applicationUser.Id, email, firstName, lastName);
    }
}
