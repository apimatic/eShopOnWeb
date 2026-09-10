using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against a captured payment. A capture may have several partial refunds,
/// each carrying the caller-supplied idempotency key that produced it so a repeat request under the
/// same key is never refunded twice.
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

    /// <summary>The caller-supplied idempotency key for this refund request.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal's id for the refund.</summary>
    public string RefundId { get; private set; }

    /// <summary>The refunded amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's reported refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
