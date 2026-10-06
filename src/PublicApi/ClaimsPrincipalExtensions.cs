using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    private static readonly string[] IdentityClaimTypes =
    {
        ClaimTypes.Name,
        "name",
        "unique_name",
        ClaimTypes.NameIdentifier,
        "sub"
    };

    public static string? GetAuthenticatedUserName(this ClaimsPrincipal principal)
    {
        foreach (var claimType in IdentityClaimTypes)
        {
            var value = principal.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return principal.Identity?.Name;
    }
}
