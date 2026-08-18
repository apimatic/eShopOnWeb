using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>Reads the caller's identity out of the validated JWT. The token's name claim is the shopper's username/email.</summary>
public static class CallerIdentity
{
    /// <summary>The signed-in shopper's identity (their email/username), or null if unauthenticated.</summary>
    public static string? GetBuyerId(this ClaimsPrincipal? user)
    {
        if (user is null) return null;
        return user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
    }
}
