using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ----- Place order -----

public class CreateOrderRequest
{
    /// <summary>Catalog items and quantities to order. Prices come from the catalog, currency USD.</summary>
    public List<OrderLineDto> Items { get; set; } = new();

    /// <summary>Optional shipping address; a placeholder is used when omitted.</summary>
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

/// <summary>Response for a placed order. <c>OrderId</c> is a top-level identifier for follow-up calls.</summary>
public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

// ----- Pay for an order -----

/// <summary>Provide exactly one of <see cref="Card"/> (one-off) or <see cref="SavedPaymentMethodId"/>.</summary>
public class PayOrderBody
{
    public CardDto? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

public class PayOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? PayPalOrderId { get; set; }
    public string? CaptureId { get; set; }
}

// ----- Refund an order -----

public class RefundOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? RefundId { get; set; }
}

// ----- My orders -----

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
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
