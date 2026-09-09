using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public static class PaymentEndpointHelpers
{
    /// <summary>The shopper's identity from the JWT — the same string the app uses as Order.BuyerId.</summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            throw new PaymentException("The authenticated user has no identity.", 401);
        }
        return buyerId;
    }

    /// <summary>Map a request address, or fall back to a default when none is supplied.</summary>
    public static Address ToAddress(this AddressRequest? request) => request is null
        ? new Address("123 Main St.", "Kent", "OH", "United States", "44240")
        : new Address(request.Street, request.City, request.State, request.Country, request.ZipCode);
}
