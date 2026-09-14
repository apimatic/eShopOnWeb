using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the caller's stable identity from JWT claims for use as the Maxio customer
/// <c>reference</c>. Prefers the user id (<see cref="ClaimTypes.NameIdentifier"/>) since it
/// never changes for a given account; falls back to username for tokens minted before that
/// claim was added.
/// </summary>
public static class SubscriberIdentity
{
    public static string? GetReference(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(ClaimTypes.Name);

    public static string? GetEmail(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue(ClaimTypes.Name);
}
