using System.Security.Claims;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Reads the caller's identity from the validated JWT. The user name is the buyer id.</summary>
public static class CallerIdentity
{
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return buyerId!;
    }
}
