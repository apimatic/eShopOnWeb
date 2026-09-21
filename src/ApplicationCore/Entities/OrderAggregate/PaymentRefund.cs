using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund taken against a captured <see cref="OrderPayment"/>. Each refund records the
/// caller-supplied idempotency key so a repeated request under the same key is never refunded twice,
/// while two distinct keys remain two legitimate partial refunds.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, string refundId, decimal amount, string status)
    {
        IdempotencyKey = idempotencyKey;
        RefundId = refundId;
        Amount = amount;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>The PayPal-generated refund id.</summary>
    public string RefundId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal's refund status wire value (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A refund counts against the captured total unless PayPal explicitly failed/cancelled it.</summary>
    public bool CountsTowardRefundedTotal =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}
