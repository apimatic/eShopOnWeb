using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class CallerIdentity
{
    /// <summary>
    /// Resolves the authenticated caller's user name (an e-mail in eShopOnWeb) from the JWT.
    /// The token issued by this API carries it as the <see cref="ClaimTypes.Name"/> claim.
    /// </summary>
    public static string ResolveUserName(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name
                   ?? user.FindFirstValue(ClaimTypes.Name)
                   ?? user.FindFirstValue("unique_name");

        if (string.IsNullOrWhiteSpace(name))
        {
            throw SubscriptionBillingException.BadRequest(
                "The authentication token does not contain a user identity.");
        }

        return name;
    }
}
