using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Reads the signed-in caller's identity and request-lifetime token from the current HTTP context. The
/// caller's identity always comes from the token (the <see cref="ClaimTypes.Name"/> claim == the username),
/// never from the request body.
/// </summary>
internal static class CallerIdentity
{
    public static string? GetUserName(this IHttpContextAccessor accessor)
        => accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);

    public static CancellationToken RequestAborted(this IHttpContextAccessor accessor)
        => accessor.HttpContext?.RequestAborted ?? CancellationToken.None;
}
