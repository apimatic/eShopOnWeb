using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Reads the caller's identity from the validated JWT. The token carries the user name
/// as the name claim and each role as a role claim (see IdentityTokenClaimService), so
/// the buyer id used to scope a shopper's own data is simply the caller's user name.
/// </summary>
public static class CallerExtensions
{
    public static string? GetBuyerId(this ClaimsPrincipal user)
        => user?.Identity?.Name;

    public static bool IsOperator(this ClaimsPrincipal user)
        => user is not null && user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
