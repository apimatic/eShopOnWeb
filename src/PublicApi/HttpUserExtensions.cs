using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

public static class HttpUserExtensions
{
    public static string RequireBuyerId(this HttpContext httpContext)
    {
        var buyerId = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw OrderPaymentException.Forbidden("A signed-in shopper is required.");
        }

        return buyerId;
    }

    public static bool IsAdministrator(this HttpContext httpContext) =>
        httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
