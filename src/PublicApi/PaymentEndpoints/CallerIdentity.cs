using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class CallerIdentity
{
    public static string GetBuyerId(HttpContext context)
    {
        var name = context.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentOperationException(401, "The caller could not be identified from the token.");
        }

        return name;
    }

    public static bool IsAdministrator(HttpContext context)
    {
        return context.User?.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS) == true;
    }
}
