using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentityResolver
{
    public static async Task<ShopperIdentity> ResolveAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager)
    {
        var principal = httpContext.User;
        var userName = principal.Identity?.Name
                       ?? principal.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingValidationException("The access token does not identify a shopper.");
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new BillingValidationException($"No shopper account matches '{userName}'.");
        }

        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : userName;
        var localPart = email.Split('@')[0];
        var firstName = SanitizeName(localPart);
        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = "Shopper";
        }

        return new ShopperIdentity(user.Id, email, firstName, "Shopper");
    }

    private static string SanitizeName(string value)
    {
        var cleaned = new string(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or ' ' or '\'').ToArray());
        cleaned = string.IsNullOrWhiteSpace(cleaned) ? value : cleaned.Trim();
        return cleaned.Length <= 40 ? cleaned : cleaned[..40];
    }
}
