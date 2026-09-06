using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Reads the caller's eShopOnWeb user name out of their bearer token.
/// </summary>
/// <remarks>
/// Subscription endpoints never accept a user identifier from the request body or query string: who is
/// billed is decided by the token alone, so one shopper cannot enroll or inspect another. Tokens issued
/// by <c>api/authenticate</c> carry the user name in the standard name claim; the alternatives below
/// cover tokens minted by an identity provider that uses the JWT short names instead.
/// </remarks>
public static class CallerIdentity
{
    public static string? GetUserName(this ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var name = user.Identity.Name
                   ?? user.FindFirstValue(ClaimTypes.Name)
                   ?? user.FindFirstValue("unique_name")
                   ?? user.FindFirstValue(ClaimTypes.Email)
                   ?? user.FindFirstValue("email")
                   ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? user.FindFirstValue("sub");

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
