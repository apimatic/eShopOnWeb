using System.Threading;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Small helpers for reading the authenticated caller's identity and request-cancellation token inside
/// endpoint handlers. The caller's identity always comes from the token (never a request field).
/// </summary>
internal static class EndpointCaller
{
    public static string? UserName(IHttpContextAccessor accessor) => accessor.HttpContext?.User?.Identity?.Name;

    public static CancellationToken RequestAborted(IHttpContextAccessor accessor)
        => accessor.HttpContext?.RequestAborted ?? CancellationToken.None;
}
