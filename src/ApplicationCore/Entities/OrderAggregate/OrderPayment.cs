using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Tracks the Stax payment (and refund) attached to an order. One row per order.
/// </summary>
public class OrderPayment : BaseEntity
{
    public const string ATTEMPTING = "Attempting";
    public const string PROCESSING = "Processing";
    public const string PAID = "Paid";
    public const string FAILED = "Failed";

    public const string REFUND_NONE = "none";
    public const string REFUND_PENDING = "pending";
    public const string REFUND_REFUNDED = "refunded";
    public const string REFUND_FAILED = "failed";

    public int OrderId { get; set; }
    public string PaymentMethodId { get; set; } = string.Empty;
    /// <summary>"card" or "bank"</summary>
    public string Method { get; set; } = string.Empty;
    public string? TransactionId { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = ATTEMPTING;
    /// <summary>Incremented only after Stax definitively rejected a charge, so retries after a transport failure reuse the idempotency key.</summary>
    public int Attempt { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }

    public string RefundStatus { get; set; } = REFUND_NONE;
    public DateTimeOffset? RefundRequestedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public string? RefundMessage { get; set; }
    public int RefundAttempts { get; set; }
}
