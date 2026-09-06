using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsUtility
{
    public static string GetUserIdFromClaims(ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.Identity?.Name
            ?? throw new InvalidOperationException("User ID not found in claims");
        return userId;
    }
}
