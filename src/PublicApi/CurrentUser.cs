using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class CurrentUser
{
    public static string Require(HttpContext http)
    {
        var name = http.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new CheckoutException(401, "A signed-in shopper is required.");
        return name;
    }
}
