using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class CurrentBillingUser
{
    public static async Task<BillingUser?> ResolveAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = !string.IsNullOrWhiteSpace(userId)
            ? await userManager.FindByIdAsync(userId)
            : await userManager.FindByNameAsync(httpContext.User.Identity?.Name ?? string.Empty);
        if (user is null) return null;

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email)) return null;

        var localPart = email.Split('@', 2)[0];
        var nameParts = localPart
            .Split(new[] { '.', '-', '_', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeName)
            .Where(x => x.Length > 0)
            .Take(2)
            .ToArray();
        var firstName = nameParts.ElementAtOrDefault(0) ?? "eShop";
        var lastName = nameParts.ElementAtOrDefault(1) ?? "Customer";

        return new BillingUser(user.Id, email, firstName, lastName);
    }

    private static string NormalizeName(string value)
    {
        var letters = new string(value.Where(char.IsLetterOrDigit).Take(50).ToArray());
        if (letters.Length == 0) return string.Empty;
        return char.ToUpperInvariant(letters[0]) + letters[1..];
    }
}
