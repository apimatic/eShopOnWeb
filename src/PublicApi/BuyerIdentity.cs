using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class BuyerIdentity
{
    public static IResult? RequireBuyer(ClaimsPrincipal user, out string buyerId)
    {
        buyerId = user.Identity?.Name ?? string.Empty;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        return null;
    }
}
