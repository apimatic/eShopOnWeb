using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public static class PaymentStatuses
{
    public const string Processing = "processing";
    /// <summary>Accepted by Stax but not settled yet (bank payments).</summary>
    public const string Pending = "pending";
    public const string Paid = "paid";
    public const string Failed = "failed";
}

public static class RefundStatuses
{
    public const string None = "none";
    /// <summary>Requested by staff; Stax cannot refund yet, the shop retries until it can.</summary>
    public const string Pending = "pending";
    public const string Refunded = "refunded";
    public const string Failed = "failed";
}

/// <summary>The payment (and refund) state of an order.</summary>
public class OrderPayment
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string StaxPaymentMethodId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string? StaxTransactionId { get; set; }
    public decimal OrderTotal { get; set; }
    public decimal Fee { get; set; }
    public decimal ChargedTotal { get; set; }
    public string Status { get; set; } = PaymentStatuses.Processing;
    public string? FailureMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }

    public string RefundStatus { get; set; } = RefundStatuses.None;
    public decimal? RefundAmount { get; set; }
    public string? StaxRefundTransactionId { get; set; }
    public string? RefundMessage { get; set; }
    public DateTimeOffset? RefundRequestedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public DateTimeOffset? NextRefundAttemptAt { get; set; }
    public int RefundAttempts { get; set; }
}
