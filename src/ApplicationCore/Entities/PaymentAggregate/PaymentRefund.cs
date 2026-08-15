using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against a captured <see cref="Payment"/>. A capture may be refunded
/// more than once (distinct partial refunds), so refunds are modelled as a child collection.
/// </summary>
public class PaymentRefund : BaseEntity
{
    /// <summary>PayPal's own id for this refund.</summary>
    public string PayPalRefundId { get; private set; }

    /// <summary>The refunded amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's current status for the refund (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>
    /// The caller-supplied idempotency key for this refund. Repeating a request under the same
    /// key must not refund twice; two distinct keys are two legitimate partial refunds.
    /// </summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
    }
}
