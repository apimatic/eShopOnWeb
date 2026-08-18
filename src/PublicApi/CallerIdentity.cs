using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// The caller's identity, taken from the JWT (the name claim). This is the shopper/buyer id every
/// shopper-scoped endpoint acts under.
/// </summary>
public static class CallerIdentity
{
    public static string? Of(ClaimsPrincipal? user) => user?.Identity?.Name;
}
