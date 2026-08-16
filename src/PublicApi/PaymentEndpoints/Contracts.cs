using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---------------- Shared card / address shapes ----------------

/// <summary>Raw card details for a one-off payment or to vault. Never stored or logged by this app.</summary>
public class CardRequest
{
    /// <summary>Primary account number, e.g. the sandbox Visa 4111111111111111.</summary>
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in YYYY-MM (e.g. 2027-04). Any future date for the sandbox card.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

// ---------------- Place order ----------------

public class PlaceOrderRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipToAddress { get; set; }
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderResponse
{
    /// <summary>Top-level identifier of the created order, so the flow can be driven end to end.</summary>
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

// ---------------- Pay (authorize) ----------------

public class PayOrderRequest
{
    public int OrderId { get; set; } // bound from the route
    /// <summary>Card details for a one-off payment. Provide this or <see cref="SavedPaymentMethodId"/>, not both.</summary>
    public CardRequest? Card { get; set; }
    /// <summary>Id of one of the shopper's saved cards to pay with instead.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

// ---------------- Fulfil / Cancel (operator, route only) ----------------

public class OrderActionRequest
{
    public int OrderId { get; set; }
}

// ---------------- Refund ----------------

public class RefundOrderRequest
{
    public int OrderId { get; set; } // bound from the route
    /// <summary>Partial refund amount; omit to refund the full remaining captured amount.</summary>
    public decimal? Amount { get; set; }
    /// <summary>Caller-supplied idempotency key. Repeats under the same key never refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    /// <summary>Top-level identifier of the refund.</summary>
    public Guid RefundId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? PayPalRefundId { get; set; }
    public decimal CapturedAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
}

// ---------------- My orders ----------------

public class MyOrdersRequest { }

// ---------------- Save payment method ----------------

public class SavePaymentMethodRequest
{
    public CardRequest Card { get; set; } = new();
}

public class SavePaymentMethodResponse
{
    /// <summary>Top-level identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    /// <summary>A safe, human-recognisable description, e.g. "VISA ****1111".</summary>
    public string Display { get; set; } = string.Empty;
}

public class ListPaymentMethodsRequest { }

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public string Display { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class DeletePaymentMethodRequest
{
    public int PaymentMethodId { get; set; }
}

// ---------------- Reconciliation ----------------

public class ReconciliationRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}
