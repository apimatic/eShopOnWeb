using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingEndpointUser
{
    public static async Task<BillingUser> ResolveAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        var userName = context.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingValidationException("The authenticated user identity is unavailable.");
        }

        var applicationUser = await userManager.FindByNameAsync(userName);
        var email = applicationUser?.Email ?? applicationUser?.UserName;
        if (applicationUser is null || string.IsNullOrWhiteSpace(applicationUser.Id) || string.IsNullOrWhiteSpace(email))
        {
            throw new BillingValidationException("The authenticated user profile is incomplete.");
        }

        var localPart = email.Split('@', 2)[0];
        var nameParts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = NormalizeName(nameParts.FirstOrDefault() ?? "eShop");
        var lastName = NormalizeName(nameParts.Skip(1).FirstOrDefault() ?? "Customer");
        return new BillingUser(applicationUser.Id, email, firstName, lastName);
    }

    private static string NormalizeName(string value) =>
        value.Length == 0 ? "Customer" : char.ToUpperInvariant(value[0]) + value[1..];
}
