using System;

using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public static class PaymentStatus
{
    public const string Unpaid = "unpaid";
    public const string Processing = "processing";
    /// <summary>Accepted by Stax, but the bank has not settled it yet.</summary>
    public const string Pending = "pending";
    public const string Paid = "paid";
    /// <summary>The bank rejected or returned the payment.</summary>
    public const string Failed = "failed";
}

public static class RefundState
{
    public const string None = "none";
    public const string Pending = "pending";
    public const string Refunded = "refunded";
    public const string Failed = "failed";
}

/// <summary>
/// Payment and refund bookkeeping for an order (one row per order).
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; set; }
    public string BuyerEmail { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = PaymentStatus.Unpaid;
    public string? StaxPaymentMethodId { get; set; }
    /// <summary>"card" or "bank"</summary>
    public string? Method { get; set; }
    public string? StaxTransactionId { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? LastError { get; set; }

    public string RefundStatus { get; set; } = RefundState.None;
    public decimal? RefundAmount { get; set; }
    public DateTimeOffset? RefundRequestedAt { get; set; }
    public DateTimeOffset? RefundLastAttemptAt { get; set; }
    public int RefundAttempts { get; set; }
}
