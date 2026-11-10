using System.Security.Claims;
using Microsoft.eShopWeb.PublicApi.Banking;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

internal static class BankEndpointSupport
{
    /// <summary>Who is calling: the identity in the JWT. Never taken from the request body or route.</summary>
    public static string ShopperOf(ClaimsPrincipal user) =>
        user.Identity?.Name is { Length: > 0 } name
            ? name.Trim().ToLowerInvariant() // shoppers are keyed by lower-cased name, whatever the case in the token
            : throw new RequestFailedException(401, "The token carries no identity.");
}
