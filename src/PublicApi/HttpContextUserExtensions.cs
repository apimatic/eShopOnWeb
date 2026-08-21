using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpContextUserExtensions
{
    public static string? GetBuyerId(this ClaimsPrincipal user)
    {
        return user.Identity?.Name;
    }

    public static IResult? RequireBuyerId(this ClaimsPrincipal user, out string buyerId)
    {
        buyerId = user.Identity?.Name ?? string.Empty;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        return null;
    }
}
