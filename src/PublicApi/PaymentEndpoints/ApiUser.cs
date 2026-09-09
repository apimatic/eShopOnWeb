using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Helpers for deriving the caller's identity from the validated JWT.</summary>
public static class ApiUser
{
    /// <summary>
    /// The shopper's stable identity (their username/email), used as the order/buyer key. Every
    /// shopper-scoped endpoint acts only on data belonging to this identity.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("unique_name")
            ?? user.Identity?.Name
            ?? string.Empty;
    }
}
