using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>Resolves the calling shopper's identity from the validated JWT.</summary>
public static class CallerIdentity
{
    /// <summary>
    /// The buyer identity used to scope orders and saved cards. Taken from the token's name claim so a
    /// caller only ever sees or acts on their own data.
    /// </summary>
    public static string BuyerId(ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.Name)
            ?? user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "unknown";
    }
}
