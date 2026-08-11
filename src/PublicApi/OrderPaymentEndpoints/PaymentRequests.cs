using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>Card details for a one-off payment or for saving a card. Never persisted or logged.</summary>
public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in YYYY-MM form.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }

    public CardDetails ToCardDetails() => new(
        Number: Number,
        Expiry: Expiry,
        SecurityCode: SecurityCode,
        CardholderName: string.IsNullOrWhiteSpace(CardholderName) ? "Cardholder" : CardholderName!,
        BillingAddressLine1: BillingAddressLine1,
        BillingCity: BillingCity,
        BillingState: BillingState,
        BillingPostalCode: BillingPostalCode,
        BillingCountryCode: BillingCountryCode);
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PlaceOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipToAddress { get; set; }

    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class PayOrderRequest
{
    /// <summary>Card details for a one-off payment. Provide this OR SavedPaymentMethodId, not both.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards to pay with.</summary>
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class RefundOrderRequest
{
    /// <summary>Caller-supplied idempotency key: a repeat under the same key does not refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Amount to refund. Omit for a full refund of the remaining captured balance.</summary>
    public decimal? Amount { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

/// <summary>Used by no-body operator actions (fulfil / cancel).</summary>
public class OrderActionRequest
{
    public OrderActionRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; }
}
