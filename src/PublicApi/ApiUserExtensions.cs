using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class ApiUserExtensions
{
    public static string? GetBuyerId(this ClaimsPrincipal user) => user.Identity?.Name;

    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

    public static IResult? RequireBuyerId(this ClaimsPrincipal user, out string buyerId)
    {
        buyerId = user.GetBuyerId() ?? string.Empty;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        return null;
    }
}
