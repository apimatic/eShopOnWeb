using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Derives the subscriber's identity from the bearer token. The caller never gets to name who they
/// are subscribing: the identity always comes from the validated JWT.
/// </summary>
internal static class SubscriberIdentity
{
    /// <summary>The authenticated user name, or null when the principal carries no name claim.</summary>
    public static string? GetUserName(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? principal.FindFirstValue(ClaimTypes.Name) : name;
    }

    /// <summary>
    /// Normalises a user name into the stable key that provider-side references are derived from.
    /// eShopOnWeb user names are email addresses, which are case-insensitive in practice.
    /// </summary>
    public static string ToUserKey(string userName) => userName.Trim().ToLowerInvariant();
}
