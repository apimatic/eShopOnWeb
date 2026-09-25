using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ----- Requests -----

public record OrderLineRequest
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public record ShippingAddressRequest
{
    public string Street { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string? State { get; init; }
    public string Country { get; init; } = string.Empty;
    public string ZipCode { get; init; } = string.Empty;
}

public record PlaceOrderRequest
{
    public List<OrderLineRequest> Items { get; init; } = new();
    public ShippingAddressRequest ShipTo { get; init; } = new();
}

/// <summary>Card details for a one-off payment or to save a card. Never persisted or logged.</summary>
public record CardRequestDto
{
    public string Number { get; init; } = string.Empty;
    /// <summary>ISO-8601 YYYY-MM.</summary>
    public string Expiry { get; init; } = string.Empty;
    public string? SecurityCode { get; init; }
    public string? CardholderName { get; init; }
    public string? BillingAddressLine1 { get; init; }
    public string? BillingAddressLine2 { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingState { get; init; }
    public string? BillingPostalCode { get; init; }
    public string? BillingCountryCode { get; init; }

    public CardDetails ToCardDetails() => new()
    {
        Number = Number,
        Expiry = Expiry,
        SecurityCode = SecurityCode,
        CardholderName = CardholderName,
        BillingAddressLine1 = BillingAddressLine1,
        BillingAddressLine2 = BillingAddressLine2,
        BillingCity = BillingCity,
        BillingState = BillingState,
        BillingPostalCode = BillingPostalCode,
        BillingCountryCode = BillingCountryCode
    };
}

public record PayOrderRequest
{
    public CardRequestDto? Card { get; init; }
    public int? SavedPaymentMethodId { get; init; }
}

public record RefundOrderRequest
{
    /// <summary>Amount to refund; null refunds the full remaining captured amount.</summary>
    public decimal? Amount { get; init; }
    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key never refunds twice.</summary>
    public string? IdempotencyKey { get; init; }
}

// ----- Responses (top-level identifiers per the task) -----

public record PlaceOrderResponse
{
    public int OrderId { get; init; }
}

public record RefundOrderResponse
{
    public string RefundId { get; init; } = string.Empty;
    public PaymentView Payment { get; init; } = null!;
}
