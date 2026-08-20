using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string? GetBuyerId(this HttpContext http) =>
        http.User.Identity?.Name;

    public static bool IsAdministrator(this HttpContext http) =>
        http.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

    public static T GetRequired<T>(this HttpContext http) where T : notnull =>
        http.RequestServices.GetRequiredService<T>();
}
