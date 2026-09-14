using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The eShopOnWeb user behind a request, resolved from the JWT and the Identity store. The
/// <see cref="UserReference"/> (the Identity user id) is the stable key used to map this user to
/// a billing customer.
/// </summary>
public record CurrentUser(string UserReference, string Email, string FirstName, string LastName);

/// <summary>
/// Resolves the authenticated caller from the bearer token. The token only carries the user name;
/// the stable user id and email are read from the Identity store.
/// </summary>
public static class CurrentUserResolver
{
    public static async Task<CurrentUser?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
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
        // ApplicationUser has no first/last name; derive a reasonable pair from the email local part.
        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;

        return new CurrentUser(user.Id, email, firstName, "eShopOnWeb");
    }
}
