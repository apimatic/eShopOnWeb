using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Helpers for reading the caller's identity from the JWT. The buyer id used throughout the app is
/// the user name, which is what the token carries.
/// </summary>
public static class CurrentUser
{
    /// <summary>
    /// The signed-in caller's user name, or null when the token carries no usable name claim.
    /// Reads across the claim types the token may use regardless of inbound claim mapping.
    /// </summary>
    public static string? GetUserName(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return null;
        }

        var name = user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue(JwtRegisteredClaimNames.UniqueName)
            ?? user.FindFirstValue("name");

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
