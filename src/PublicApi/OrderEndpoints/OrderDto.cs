using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>An order and its payment state, safe to return to the owning shopper.</summary>
public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>Payment lifecycle: AwaitingPayment, Paid or Refunded.</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    /// <summary>Safe description of the card used, e.g. "VISA ending in 1111". Null until paid.</summary>
    public string? PaymentCardDescription { get; set; }

    public string? PayPalOrderId { get; set; }
    public string? PayPalCaptureId { get; set; }
    public string? PayPalRefundId { get; set; }
    public DateTimeOffset? PaidDate { get; set; }
    public DateTimeOffset? RefundedDate { get; set; }

    public List<OrderItemDto> Items { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
