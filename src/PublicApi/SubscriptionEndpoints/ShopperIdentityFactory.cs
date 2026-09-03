using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentityFactory
{
    public static async Task<ShopperIdentity?> FromUserAsync(
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

        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : userName;
        return new ShopperIdentity(
            UserId: user.Id,
            Email: email,
            FirstName: DeriveFirstName(email),
            LastName: "Shopper");
    }

    private static string DeriveFirstName(string email)
    {
        var local = email.Split('@')[0];
        var builder = new StringBuilder();
        foreach (var ch in local)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
        }

        var name = builder.ToString();
        return string.IsNullOrWhiteSpace(name) ? "Shopper" : name;
    }
}
