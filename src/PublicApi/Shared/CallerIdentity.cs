using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Shared;

public static class CallerIdentity
{
    public static string Get(HttpContext? httpContext)
    {
        var name = httpContext?.User?.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            throw new ApiException("The caller identity could not be determined from the token.", 401);
        }

        return name;
    }
}