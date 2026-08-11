using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against a captured payment. A capture may be refunded more
/// than once (several partial refunds), so refunds are modelled as child records of
/// the <see cref="Payment"/> aggregate.
/// </summary>
public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    public PaymentRefund(string paypalRefundId, decimal amount, string idempotencyKey, string status)
    {
        PayPalRefundId = paypalRefundId;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The refund id PayPal assigned; also returned to the caller as <c>refundId</c>.</summary>
    public string PayPalRefundId { get; private set; } = default!;

    public decimal Amount { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; } = default!;

    /// <summary>PayPal's refund status (COMPLETED, PENDING, ...).</summary>
    public string Status { get; private set; } = default!;

    public DateTimeOffset CreatedAt { get; private set; }

    public int PaymentId { get; private set; }
}
