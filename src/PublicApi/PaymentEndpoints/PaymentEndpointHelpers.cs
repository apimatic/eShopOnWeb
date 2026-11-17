using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public static class PaymentEndpointHelpers
{
    public static string GetBuyerId(HttpContext context)
    {
        var name = context.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentOperationException(401, "A signed-in shopper is required.");
        }

        return name;
    }
}
