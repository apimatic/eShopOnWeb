using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class CurrentUserExtensions
{
    /// <summary>
    /// Resolves the ApplicationUser from the JWT's name claim.
    /// </summary>
    public static async Task<ApplicationUser?> GetApplicationUserAsync(
        this ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var username = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }
        return await userManager.FindByNameAsync(username);
    }

    /// <summary>
    /// Derives first/last names for the billing customer profile from the username.
    /// eShopOnWeb identities are seeded with email-style usernames ("demouser@microsoft.com"),
    /// so the part before the @ becomes the first name and the domain the last name. Maxio
    /// Advanced Billing rejects blank last names, so a fallback is always provided.
    /// </summary>
    public static (string FirstName, string LastName) GetNameParts(this ApplicationUser user)
    {
        const string fallback = "Customer";
        var username = user.UserName ?? user.Email ?? fallback;
        var at = username.IndexOf('@');
        if (at < 0)
        {
            return (string.IsNullOrWhiteSpace(username) ? fallback : username, fallback);
        }
        var firstName = username.Substring(0, at);
        var lastName = username.Substring(at + 1);
        if (string.IsNullOrWhiteSpace(firstName)) firstName = fallback;
        if (string.IsNullOrWhiteSpace(lastName)) lastName = fallback;
        return (firstName, lastName);
    }
}
