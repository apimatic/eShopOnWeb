using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Resolves the calling shopper's identity from the validated JWT. This is the single source of truth
/// for "who is the caller" — it is never taken from the request body, so a shopper can only ever act
/// on their own orders and cards.
/// </summary>
public static class CallerIdentity
{
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.Name)
                 ?? user.Identity?.Name
                 ?? user.FindFirstValue("unique_name");

        if (string.IsNullOrEmpty(id))
        {
            throw new UnauthorizedAccessException("The access token does not identify a user.");
        }

        return id;
    }
}
