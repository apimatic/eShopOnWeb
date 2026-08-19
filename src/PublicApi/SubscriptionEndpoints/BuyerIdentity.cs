using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BuyerIdentity
{
    public static async Task<(ApplicationUser? User, IResult? Failure)> ResolveAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return (null, Results.Unauthorized());
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return (null, Results.Unauthorized());
        }

        return (user, null);
    }

    public static (string FirstName, string LastName, string Email) Describe(ApplicationUser user)
    {
        var email = user.Email ?? user.UserName ?? "buyer@example.com";
        var local = email.Split('@')[0];
        var firstName = string.IsNullOrWhiteSpace(local) ? "Buyer" : local;
        return (firstName, "Customer", email);
    }
}
