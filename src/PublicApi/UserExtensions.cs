using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

internal static class UserExtensions
{
    public static string GetRequiredBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new System.InvalidOperationException("The caller's identity does not include a name claim.");
        }

        return name;
    }

    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
