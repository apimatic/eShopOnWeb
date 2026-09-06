using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the caller of a subscription endpoint from their bearer token.
/// <para>
/// The identity always comes from the token; nothing in the request body can influence which
/// account is billed. The token is then checked against Identity, so a token for a deleted account
/// cannot create billing records.
/// </para>
/// </summary>
public static class SubscriberIdentityResolver
{
    public static async Task<SubscriberIdentity?> ResolveAsync(ClaimsPrincipal? principal, UserManager<ApplicationUser> userManager)
    {
        var userName = GetUserName(principal);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user?.UserName is null)
        {
            return null;
        }

        return new SubscriberIdentity(user.UserName, user.Email ?? user.UserName);
    }

    private static string? GetUserName(ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(principal.Identity?.Name))
        {
            return principal.Identity!.Name;
        }

        // Depending on inbound claim mapping the name can arrive under its short JWT form.
        return new[] { ClaimTypes.Name, JwtRegisteredClaimNames.UniqueName, JwtRegisteredClaimNames.Sub, ClaimTypes.NameIdentifier }
            .Select(principal.FindFirstValue)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
