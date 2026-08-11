using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class PaymentEndpointHelpers
{
    /// <summary>The caller's identity is the buyer id, taken from the token (never from the request body).</summary>
    public static string? GetBuyerId(ClaimsPrincipal user) => user.Identity?.Name;

    /// <summary>
    /// Build a shipping address for the order. The payment surface does not require a real address, so a
    /// placeholder is used when one is not supplied (or a field is missing) to satisfy the order model.
    /// </summary>
    public static Address ToAddress(ShippingAddressDto? dto)
    {
        const string placeholder = "N/A";
        return new Address(
            street: NonEmpty(dto?.Street, placeholder),
            city: NonEmpty(dto?.City, placeholder),
            state: dto?.State ?? string.Empty,
            country: NonEmpty(dto?.Country, placeholder),
            zipcode: NonEmpty(dto?.ZipCode, placeholder));
    }

    private static string NonEmpty(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value!;
}
