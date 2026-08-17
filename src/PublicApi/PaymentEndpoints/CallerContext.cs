using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>The authenticated caller, resolved from the JWT. Identity always comes from the token.</summary>
public sealed record CallerContext(string Username, bool IsAdmin)
{
    public static CallerContext From(ClaimsPrincipal user)
    {
        var username = user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("unique_name")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        // Endpoints are behind [Authorize]; a null name would indicate a misconfigured token.
        return new CallerContext(username ?? string.Empty, user.IsInRole(Constants.Roles.ADMINISTRATORS));
    }
}
