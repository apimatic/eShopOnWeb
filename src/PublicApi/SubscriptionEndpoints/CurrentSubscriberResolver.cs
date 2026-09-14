using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The eShopOnWeb user behind an authenticated request, resolved from the JWT identity.
/// </summary>
public record CurrentSubscriber(string Reference, string Email, string UserName);

/// <summary>
/// Resolves the authenticated caller (from the bearer token) to a stable eShopOnWeb user.
/// The user's identity id is used as the Maxio customer <c>reference</c> (idempotency key),
/// which is stable even if the email/username changes.
/// </summary>
public static class CurrentSubscriberResolver
{
    public static async Task<CurrentSubscriber?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName)
            ?? await userManager.FindByEmailAsync(userName);

        if (user is null)
        {
            return null;
        }

        return new CurrentSubscriber(
            Reference: user.Id,
            Email: user.Email ?? userName,
            UserName: user.UserName ?? userName);
    }
}
