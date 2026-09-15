using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class CallerIdentity
{
    /// <summary>
    /// The caller's identity (username) taken from the JWT. Every shopper-scoped endpoint keys
    /// its data off this so a caller can only ever see or act on their own data.
    /// </summary>
    public static string RequireUsername(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(name))
        {
            // The endpoints are all [Authorize]d, so this should not happen in practice.
            throw new System.UnauthorizedAccessException("The bearer token does not identify a user.");
        }
        return name;
    }
}
