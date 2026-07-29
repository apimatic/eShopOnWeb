using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The billing identity of the authenticated caller, derived from the JWT and the
/// Identity store. <see cref="Reference"/> is the eShopOnWeb user id and is used as
/// the stable Maxio customer reference.
/// </summary>
public sealed record SubscriberIdentity(string Reference, string Email, string FirstName, string LastName)
{
    /// <summary>
    /// Resolves the caller's billing identity from their token. Returns null when
    /// the token carries no usable identity or the user cannot be found.
    /// </summary>
    public static async Task<SubscriberIdentity?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = user.Email ?? userName;
        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;

        // ApplicationUser has no name fields; synthesize a sensible customer name.
        return new SubscriberIdentity(
            Reference: user.Id,
            Email: email,
            FirstName: string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart,
            LastName: "eShopOnWeb Customer");
    }
}
