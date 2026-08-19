using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Resolves the caller's identity from the validated JWT. The identity (username) is the
/// buyer id used across the app; it always comes from the token, never from the request body.
/// </summary>
public static class CallerIdentity
{
    public static string GetUserName(IHttpContextAccessor accessor)
    {
        var user = accessor.HttpContext?.User;
        var name = user?.FindFirstValue(ClaimTypes.Name) ?? user?.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException("The request is not associated with an authenticated user.");
        }
        return name;
    }
}
