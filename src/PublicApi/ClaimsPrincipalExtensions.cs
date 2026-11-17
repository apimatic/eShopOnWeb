using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    public static string GetBuyerId(this ClaimsPrincipal principal)
    {
        var buyerId = principal.FindFirst(ClaimTypes.Name)?.Value ?? principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentForbiddenException("The access token does not contain a caller identity.");
        }

        return buyerId;
    }

    public static bool IsAdministrator(this ClaimsPrincipal principal) =>
        principal.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
