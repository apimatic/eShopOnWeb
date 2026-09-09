using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's identity as carried by the JWT (the <see cref="ClaimTypes.Name"/> claim the auth
    /// endpoint issues). This is the buyer/owner id every shopper-scoped operation acts on.
    /// </summary>
    public static string GetUserId(this ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.Name)
            ?? user.Identity?.Name
            ?? string.Empty;
    }
}
