using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Resolves the caller's identity (their username) from the JWT for shopper-scoped endpoints.</summary>
internal static class EndpointUser
{
    /// <summary>The caller's username (the token's Name claim), or null if unauthenticated.</summary>
    public static string? Name(IHttpContextAccessor accessor) =>
        accessor.HttpContext?.User?.Identity?.Name
            ?? accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);
}
