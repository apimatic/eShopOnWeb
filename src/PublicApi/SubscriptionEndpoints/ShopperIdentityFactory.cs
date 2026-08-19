using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentityFactory
{
    public static async Task<ShopperIdentity?> FromUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
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

        var email = user.Email ?? user.UserName ?? userName;
        var localPart = email.Split('@')[0];
        var firstName = SanitizeName(localPart);
        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = "eShop";
        }

        return new ShopperIdentity(email, firstName, "Shopper");
    }

    private static string SanitizeName(string value)
    {
        var letters = value.Where(char.IsLetterOrDigit).ToArray();
        if (letters.Length == 0)
        {
            return string.Empty;
        }

        var name = new string(letters);
        return char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant();
    }
}
