using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Shared;

/// <summary>Helpers for reading the caller's identity from the JWT.</summary>
public static class CallerIdentity
{
    /// <summary>The caller's username (the buyer id used across the order model), from the token.</summary>
    public static string? UserId(this ClaimsPrincipal principal)
        => principal.FindFirstValue(ClaimTypes.Name) ?? principal.Identity?.Name;
}
