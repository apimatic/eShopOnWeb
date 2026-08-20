using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class EndpointIdentity
{
    public static string? GetBuyerId(HttpContext http) => http.User.Identity?.Name;

    public static bool IsAdministrator(HttpContext http)
        => http.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
