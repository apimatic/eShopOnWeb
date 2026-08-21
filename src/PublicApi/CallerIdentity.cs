using Microsoft.AspNetCore.Http;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi;

internal static class CallerIdentity
{
    public static string? BuyerId(HttpContext http) => http.User.Identity?.Name;

    public static bool IsAdministrator(HttpContext http) =>
        http.User.IsInRole(Constants.Roles.ADMINISTRATORS);
}
