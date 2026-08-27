using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi;

internal static class UserExtensions
{
    public static string GetRequiredBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new System.InvalidOperationException("The caller is not authenticated.");
        }

        return buyerId;
    }

    public static bool IsAdministrator(this ClaimsPrincipal user)
        => user.IsInRole(Constants.Roles.ADMINISTRATORS);
}
