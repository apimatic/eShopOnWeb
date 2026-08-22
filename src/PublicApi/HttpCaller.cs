using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpCaller
{
    public static string? GetBuyerId(ClaimsPrincipal user) => user.Identity?.Name;

    public static IResult? RequireBuyerId(ClaimsPrincipal user, out string buyerId)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            buyerId = string.Empty;
            return Results.Unauthorized();
        }

        buyerId = name;
        return null;
    }

    public static bool IsAdministrator(ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
