using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class BuyerIdentity
{
    public static string Require(IHttpContextAccessor accessor)
    {
        var name = accessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new OrderPaymentException("The caller identity is missing from the token.", 401);
        }

        return name;
    }
}
