using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Reads the caller's identity from the validated JWT. The buyer id is the name claim.</summary>
internal static class ApiCaller
{
    public static string BuyerId(ClaimsPrincipal user) =>
        user.Identity?.Name
        ?? user.FindFirstValue(ClaimTypes.Name)
        ?? throw new System.UnauthorizedAccessException("The token does not carry a caller identity.");
}
