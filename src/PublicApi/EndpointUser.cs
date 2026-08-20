using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class EndpointUser
{
    public static string RequireBuyerId(this HttpContext http)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new ForbiddenException("The caller is not authenticated.");
        }

        return buyerId;
    }

    public static bool IsAdministrator(this ClaimsPrincipal user)
        => user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
