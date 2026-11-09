using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment (and refund) bookkeeping of one order. One row per order, created when the shopper pays.
/// </summary>
public class OrderPayment
{
    public int OrderId { get; set; }
    public string BuyerEmail { get; set; } = string.Empty;

    /// <summary>"card" or "bank".</summary>
    public string Method { get; set; } = string.Empty;
    public string PaymentMethodId { get; set; } = string.Empty;
    public string StaxCustomerId { get; set; } = string.Empty;

    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }

    public PaymentStatus Status { get; set; }
    public string? StaxTransactionId { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public int Attempts { get; set; }

    public RefundStatus RefundStatus { get; set; }
    public DateTimeOffset? RefundRequestedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public string? RefundTransactionId { get; set; }
    public string? RefundNote { get; set; }
}
