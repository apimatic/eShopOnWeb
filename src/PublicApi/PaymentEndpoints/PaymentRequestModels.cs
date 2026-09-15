using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>One line of a new order.</summary>
public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Optional shipping address for a new order.</summary>
public class AddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>Body of POST /api/orders.</summary>
public class CreateOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public AddressRequest? ShipToAddress { get; set; }
}

/// <summary>
/// Card details for a one-off payment or to save. Never stored in this app's database and never logged.
/// </summary>
public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public string? BillingLine1 { get; set; }
    public string? BillingLine2 { get; set; }
    public string? BillingState { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? CountryCode { get; set; }
}

/// <summary>Body of POST /api/orders/{orderId}/pay — either a card or a saved payment method id.</summary>
public class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

/// <summary>Body of POST /api/orders/{orderId}/refunds.</summary>
public class RefundOrderRequest
{
    /// <summary>Partial refund amount; omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating it under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>Body of POST /api/payment-methods.</summary>
public class SavePaymentMethodRequest
{
    public CardRequest Card { get; set; } = new();
}
