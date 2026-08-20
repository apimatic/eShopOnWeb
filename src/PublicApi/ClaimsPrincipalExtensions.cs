using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

internal static class ClaimsPrincipalExtensions
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new InvalidOperationException("The caller is not authenticated.");
        }

        return buyerId;
    }

    public static bool IsAdministrator(this ClaimsPrincipal user)
        => user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
