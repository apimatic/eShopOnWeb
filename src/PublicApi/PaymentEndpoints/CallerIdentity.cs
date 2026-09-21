using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Helpers for resolving the caller's identity from the validated JWT.</summary>
public static class CallerIdentity
{
    /// <summary>
    /// The buyer id for the signed-in caller, taken from the token (the name claim). Endpoints are behind
    /// [Authorize], so this is present; it is what scopes every shopper action to the caller's own data.
    /// </summary>
    public static string BuyerId(this HttpContext http)
    {
        var name = http.User.FindFirstValue(ClaimTypes.Name) ?? http.User.Identity?.Name;
        return string.IsNullOrEmpty(name)
            ? throw new System.InvalidOperationException("The authenticated caller has no name claim.")
            : name;
    }
}
