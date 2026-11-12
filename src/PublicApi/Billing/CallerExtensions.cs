using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class CallerExtensions
{
    /// <summary>The signed-in shopper's identity, taken from the JWT (never from the request).</summary>
    public static string RequireUserName(this ClaimsPrincipal user) =>
        user.Identity?.Name is { Length: > 0 } name ? name : throw new BillingException(401, "The token carries no user name.");
}
