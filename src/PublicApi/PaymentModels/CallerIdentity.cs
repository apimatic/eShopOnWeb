using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

public static class CallerIdentity
{
    /// <summary>
    /// The authenticated shopper's identity — the user name carried in the JWT's Name claim (which in
    /// this app is the email). Endpoints using this are already <c>[Authorize]</c>d, so a token is
    /// always present; returns null only in the degenerate case of a token with no name.
    /// </summary>
    public static string? GetBuyerId(this HttpContext httpContext)
    {
        return httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? httpContext.User.Identity?.Name;
    }
}
