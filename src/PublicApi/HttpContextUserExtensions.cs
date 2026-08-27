using System;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpContextUserExtensions
{
    public static string? TryGetBuyerId(this HttpContext httpContext)
    {
        return httpContext.User.Identity?.Name;
    }

    public static string GetBuyerId(this HttpContext httpContext)
    {
        var buyerId = httpContext.TryGetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            throw new InvalidOperationException("The caller is not authenticated.");
        }

        return buyerId;
    }

    public static bool IsAdministrator(this HttpContext httpContext) =>
        httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
