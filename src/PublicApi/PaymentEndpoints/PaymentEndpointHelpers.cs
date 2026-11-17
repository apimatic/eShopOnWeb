using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class PaymentEndpointHelpers
{
    public static string RequireBuyerId(IHttpContextAccessor httpContextAccessor)
    {
        var buyerId = httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentValidationException("The caller could not be identified from the token.");
        }

        return buyerId;
    }

    public static int RequireOrderId(IHttpContextAccessor httpContextAccessor)
    {
        var raw = httpContextAccessor.HttpContext?.Request.RouteValues.TryGetValue("orderId", out var value) == true
            ? value?.ToString()
            : null;

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var orderId) || orderId <= 0)
        {
            throw new PaymentValidationException("A valid orderId route value is required.");
        }

        return orderId;
    }
}
