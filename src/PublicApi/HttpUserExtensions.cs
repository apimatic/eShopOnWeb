using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CheckoutException(401, "The caller is not authenticated.");
        }

        return name;
    }

    public static string? GetIdempotencyKey(this HttpRequest request, string? bodyValue)
    {
        if (!string.IsNullOrWhiteSpace(bodyValue))
        {
            return bodyValue.Trim();
        }

        if (request.Headers.TryGetValue("Idempotency-Key", out var header) && !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString().Trim();
        }

        if (request.Headers.TryGetValue("PayPal-Request-Id", out var paypalHeader) && !string.IsNullOrWhiteSpace(paypalHeader))
        {
            return paypalHeader.ToString().Trim();
        }

        return null;
    }
}
