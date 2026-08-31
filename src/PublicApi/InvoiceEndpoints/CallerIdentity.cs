using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>Reads the caller's identity from the JWT. The token carries the username as the name claim.</summary>
internal static class CallerIdentity
{
    public static string? BuyerId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Name);
}
