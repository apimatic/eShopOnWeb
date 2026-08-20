using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpContextUserExtensions
{
    public static string RequireUserName(this HttpContext httpContext)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new OrderNotificationException("The caller is not authenticated.", 401);
        }

        return userName;
    }

    public static bool IsAdministrator(this HttpContext httpContext)
        => httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
