using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- Request bodies (bound from JSON) ----

public record OrderLineDto(int CatalogItemId, int Quantity);

public record ShipToAddressDto(string Street, string City, string State, string Country, string ZipCode);

public record PlaceOrderBody(List<OrderLineDto> Items, ShipToAddressDto? ShipToAddress);

/// <summary>Card details for a one-off payment. Never stored or logged by the application.</summary>
public record CardDto(string Number, string Expiry, string SecurityCode, string? CardholderName);

/// <summary>Pay with either a one-off <see cref="Card"/> or a previously saved <see cref="SavedPaymentMethodId"/>.</summary>
public record PayOrderBody(CardDto? Card, string? SavedPaymentMethodId);

public record RefundBody(decimal? Amount, string IdempotencyKey, string? Note);

public record SavePaymentMethodBody(CardDto Card);

// ---- Response envelopes with the required top-level identifiers ----

public record PlaceOrderApiResponse(int OrderId, decimal Total, string Currency);

public record SavePaymentMethodApiResponse(string PaymentMethodId, string? Brand, string? Last4, string? Expiry, string? CardholderName);

public record RefundApiResponse(string RefundId, string? Status, decimal Amount, string Currency, OrderPaymentView Payment);

/// <summary>Maps a request body card DTO onto the gateway's transient <see cref="CardInput"/>.</summary>
internal static class PaymentApiMapping
{
    public static CardInput ToCardInput(this CardDto dto) =>
        new(dto.Number, dto.Expiry, dto.SecurityCode, dto.CardholderName);

    /// <summary>The caller's shopper id (JWT username). Endpoints are gated by [Authorize], so this is present.</summary>
    public static string BuyerId(this ClaimsPrincipal user) =>
        user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
}
