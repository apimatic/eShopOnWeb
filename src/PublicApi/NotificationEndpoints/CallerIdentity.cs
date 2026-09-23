using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Reads the caller's identity from the JWT (never from the request body).</summary>
public static class CallerIdentity
{
    /// <summary>The signed-in shopper's identity (the token's name claim), or null if unauthenticated.</summary>
    public static string? GetUserName(this HttpContext httpContext)
    {
        var name = httpContext.User.Identity?.Name;
        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }
        return httpContext.User.FindFirstValue(ClaimTypes.Name);
    }
}
