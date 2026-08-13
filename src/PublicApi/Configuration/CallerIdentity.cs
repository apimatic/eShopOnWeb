using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Reads the caller's identity (user name) from the bearer token. Every shopper-scoped endpoint uses
/// this to act only on the caller's own data. The lookup is tolerant of the JWT claim-mapping in
/// effect, checking the usual name claim types.
/// </summary>
public static class CallerIdentity
{
    private static readonly string[] NameClaimTypes =
    {
        ClaimTypes.Name,
        "unique_name",
        "name",
        "preferred_username",
        ClaimTypes.NameIdentifier,
        "sub",
        ClaimTypes.Email,
        "email"
    };

    public static string? GetUserId(this ClaimsPrincipal? user)
    {
        if (user is null) return null;

        var direct = user.Identity?.Name;
        if (!string.IsNullOrEmpty(direct)) return direct;

        return NameClaimTypes
            .Select(user.FindFirst)
            .FirstOrDefault(c => c is not null && !string.IsNullOrEmpty(c.Value))
            ?.Value;
    }

    /// <summary>The caller's user name, or throws if the token carried no usable identity.</summary>
    public static string RequireUserId(this IHttpContextAccessor accessor)
    {
        var userId = accessor.HttpContext?.User.GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            throw new InvalidOperationException("The caller's identity could not be determined from the token.");
        }
        return userId;
    }
}
