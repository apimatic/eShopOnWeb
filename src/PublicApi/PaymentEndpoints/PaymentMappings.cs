using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Maps request DTOs onto the application layer's inputs and resolves the caller's identity.</summary>
internal static class PaymentMappings
{
    /// <summary>The buyer id is the authenticated user's name carried in the JWT (<see cref="ClaimTypes.Name"/>).</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;

    public static CardDetails ToCardDetails(this CardDto dto) => new(
        dto.Number,
        dto.Expiry,
        dto.SecurityCode,
        dto.Name,
        dto.BillingAddress is null
            ? null
            : new BillingAddressInput(
                dto.BillingAddress.AddressLine1,
                dto.BillingAddress.AddressLine2,
                dto.BillingAddress.AdminArea1,
                dto.BillingAddress.AdminArea2,
                dto.BillingAddress.PostalCode,
                dto.BillingAddress.CountryCode));

    public static OrderLineInput[] ToLines(this PlaceOrderRequest request) =>
        request.Items.Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity)).ToArray();

    public static ShippingAddressInput? ToShippingAddress(this ShippingAddressDto? dto) =>
        dto is null ? null : new ShippingAddressInput(dto.Street, dto.City, dto.State, dto.Country, dto.ZipCode);
}
