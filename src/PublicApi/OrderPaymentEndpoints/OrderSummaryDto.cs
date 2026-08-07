using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>An order and its payment state, safe to return to the shopper.</summary>
public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>One of AwaitingPayment, Paid, Refunded, Failed.</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    public string? PayPalOrderId { get; set; }
    public string? PayPalCaptureId { get; set; }
    public string? PayPalRefundId { get; set; }
    public DateTimeOffset? PaidDate { get; set; }
    public DateTimeOffset? RefundedDate { get; set; }

    public List<OrderLineDto> Items { get; set; } = new();
}
