using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Helpers for reading the caller's identity from the JWT. The buyer id is always taken from the
/// token — never from anything the caller puts in a request body — so a shopper can only ever act on
/// their own data.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("name");

        return name ?? string.Empty;
    }
}
