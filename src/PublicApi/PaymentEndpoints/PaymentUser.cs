using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Reads the caller's identity from the JWT — always the token's name claim, never the request body.</summary>
internal static class PaymentUser
{
    public static string BuyerId(ClaimsPrincipal user)
    {
        // ClaimTypes.Name carries the username the token was issued for (see IdentityTokenClaimService).
        var name = user.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            // Endpoints are [Authorize]d, so this should never happen; fail closed if it does.
            throw new Microsoft.eShopWeb.ApplicationCore.Exceptions.EntityNotFoundException(
                "The caller's identity could not be determined.");
        }
        return name;
    }
}
