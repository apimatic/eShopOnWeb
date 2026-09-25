using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund against an order's captured payment. Owned by <see cref="Order"/>.
/// The <see cref="IdempotencyKey"/> is the caller-supplied key that makes a repeated refund
/// request a no-op; it is unique per order so a resend never refunds twice, while two distinct
/// keys are two legitimate partial refunds.
/// </summary>
public class OrderRefund
{
    // Required by EF Core
    private OrderRefund() { }

    public OrderRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
    }

    /// <summary>Caller-supplied idempotency key; unique within the order.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>PayPal's refund id.</summary>
    public string PayPalRefundId { get; private set; } = string.Empty;

    /// <summary>Amount refunded by this refund.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
