using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class EndpointIdentity
{
    public static string? BuyerId(ClaimsPrincipal user) => user.Identity?.Name;

    public static bool IsAdministrator(ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

    public static IResult? RequireBuyer(ClaimsPrincipal user, out string buyerId)
    {
        buyerId = BuyerId(user) ?? string.Empty;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        return null;
    }
}
