using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new EntityNotFoundException("The caller is not authenticated.");
        }

        return name;
    }

    public static bool IsAdministrator(this ClaimsPrincipal user)
    {
        return user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
    }
}
