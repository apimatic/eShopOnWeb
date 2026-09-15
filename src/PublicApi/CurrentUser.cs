using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Reads the caller's identity from the validated JWT.</summary>
public static class CurrentUser
{
    /// <summary>
    /// The signed-in shopper's user name, taken from the token. This is the value used as an order's
    /// BuyerId and a contact number's / notification's owner, so all shopper-scoped data lines up.
    /// </summary>
    public static string? GetUserName(this ClaimsPrincipal principal)
        => principal.Identity?.Name
           ?? principal.FindFirstValue(ClaimTypes.Name)
           ?? principal.FindFirstValue("unique_name")
           ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
}
