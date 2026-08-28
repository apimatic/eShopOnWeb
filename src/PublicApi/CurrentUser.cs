using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class CurrentUser
{
    public static string BuyerId(HttpContext context)
    {
        return context.User.FindFirstValue(ClaimTypes.Name)
            ?? throw new InvalidOperationException("The authenticated token has no name claim.");
    }
}
