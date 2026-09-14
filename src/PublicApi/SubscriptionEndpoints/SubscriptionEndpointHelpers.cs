using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared identity resolution for the subscription endpoints. The PublicApi JWT only
/// carries the user's name (email-as-username); the ApplicationUser row is resolved so
/// its stable id can anchor the Maxio customer reference.
/// </summary>
internal static class SubscriptionEndpointHelpers
{
    /// <summary>
    /// Resolves the authenticated eShopOnWeb user from the token's name claim.
    /// </summary>
    public static async Task<ApplicationUser> RequireUserAsync(UserManager<ApplicationUser> userManager, ClaimsPrincipal? principal)
    {
        var userName = principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new MaxioSubscriptionException(
                StatusCodes.Status401Unauthorized,
                "A valid account is required to manage subscriptions.");
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new MaxioSubscriptionException(
                StatusCodes.Status401Unauthorized,
                "The account associated with this token no longer exists.");
        }

        return user;
    }

    /// <summary>
    /// Deterministic, stable Maxio customer reference for the user. Derived from the
    /// user's identity id so a double-submit always maps to the same Maxio customer.
    /// </summary>
    public static string BuildCustomerReference(ApplicationUser user) => "eshop-" + user.Id;
}
