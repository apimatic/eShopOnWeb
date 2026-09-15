using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string? GetUserName(this HttpContext http) =>
        http.User.Identity?.Name ?? http.User.FindFirstValue(ClaimTypes.Name);
}
