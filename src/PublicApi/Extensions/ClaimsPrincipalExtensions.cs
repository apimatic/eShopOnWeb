using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static string GetRequiredUserName(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new UnauthorizedAccessException("The caller is not authenticated.");
        }

        return name;
    }

    public static bool IsAdministrator(this ClaimsPrincipal user)
        => user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
