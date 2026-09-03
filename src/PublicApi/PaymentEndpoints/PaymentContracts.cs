using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// --- request bodies ---

public record AddressDto(string Street, string City, string State, string Country, string ZipCode);

public record OrderLineDto(int CatalogItemId, int Quantity);

public record PlaceOrderRequest(List<OrderLineDto> Items, AddressDto ShipToAddress);

/// <summary>Card details for a one-off card payment or for saving a card. Never stored or logged.</summary>
public record CardDto(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? CardholderName,
    string? BillingCountryCode,
    string? BillingPostalCode);

/// <summary>Pay an order: exactly one of <see cref="Card"/> or <see cref="SavedPaymentMethodId"/>.</summary>
public record PayOrderRequest(CardDto? Card, int? SavedPaymentMethodId);

public record RefundOrderRequest(decimal? Amount, string IdempotencyKey);

public record SaveCardRequest(CardDto Card);

// --- response bodies (top-level id fields where the task requires them) ---

public record PlaceOrderResponse(int OrderId, string Status, decimal Total, string Currency);

public record RefundResponse(string RefundId, object Payment);
