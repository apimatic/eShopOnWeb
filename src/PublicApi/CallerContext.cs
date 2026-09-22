using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Reads the authenticated caller's identity from the JWT (the token's name claim = buyer id).</summary>
internal static class CallerContext
{
    public static string GetUserName(HttpContext httpContext) =>
        httpContext.User.FindFirstValue(ClaimTypes.Name)
        ?? httpContext.User.Identity?.Name
        ?? string.Empty;

    public static bool IsAdministrator(HttpContext httpContext) =>
        httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
