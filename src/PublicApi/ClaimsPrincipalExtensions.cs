using System.Security.Claims;
using Ardalis.GuardClauses;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The caller's buyer identity, taken from the token. Matches the value stored as an order's BuyerId,
    /// so shopper-scoped queries act only on the caller's own data.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user?.Identity?.Name;
        return Guard.Against.NullOrEmpty(name, nameof(name));
    }

    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(Constants.Roles.ADMINISTRATORS);
}
