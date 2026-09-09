using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public static class CallerIdentity
{
    /// <summary>
    /// The caller's identity (username/email) taken from the JWT. Every shopper-scoped endpoint
    /// uses this so it can only ever act on the caller's own data.
    /// </summary>
    public static string Require(ClaimsPrincipal user)
    {
        var identity = user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(identity))
        {
            // Should never happen behind [Authorize], but fail closed rather than act cross-tenant.
            throw new System.UnauthorizedAccessException("The token does not identify a user.");
        }
        return identity;
    }
}
