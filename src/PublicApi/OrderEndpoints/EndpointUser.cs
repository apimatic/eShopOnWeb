using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class EndpointUser
{
    public static string RequireBuyerId(ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new ApplicationCore.Exceptions.PaymentException(
                "A signed-in shopper is required.",
                System.Net.HttpStatusCode.Unauthorized);
        }

        return buyerId;
    }

    public static bool IsAdministrator(ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
