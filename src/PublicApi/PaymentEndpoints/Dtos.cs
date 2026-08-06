using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>A requested order line: a catalog item and quantity.</summary>
public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Optional shipping address for an order. If omitted a placeholder is used.</summary>
public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>
/// Raw card details. Sent to PayPal only; never stored or logged by this application.
/// Expiry may be "YYYY-MM", "MM/YY" or "MM/YYYY".
/// </summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public CardBillingAddressDto? BillingAddress { get; set; }
}

/// <summary>Billing address for a card (mirrors the PayPal card billing_address shape).</summary>
public class CardBillingAddressDto
{
    /// <summary>2-letter ISO country code. Required by PayPal for card payments.</summary>
    public string CountryCode { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; } // city
    public string? AdminArea1 { get; set; } // state / province
    public string? PostalCode { get; set; }
}

/// <summary>A line on a placed order.</summary>
public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>An order with its payment state, as returned to the shopper.</summary>
public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? CaptureId { get; set; }
    public string? RefundId { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

/// <summary>A saved card, described safely enough to recognise it - never full card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Alias { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
}
