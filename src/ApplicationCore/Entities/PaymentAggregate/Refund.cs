using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund of a captured <see cref="Payment"/>. A capture may be refunded more than once
/// (several distinct partial refunds), so refunds are modelled as a collection under the payment.
/// The <see cref="IdempotencyKey"/> is the caller-supplied key that makes a repeated refund request
/// return the original refund instead of issuing a second one.
/// </summary>
public class Refund : BaseEntity
{
    public int PaymentId { get; private set; }

    /// <summary>PayPal's own identifier for this refund (the id a later lookup uses).</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal's current status for the refund, e.g. COMPLETED, PENDING.</summary>
    public string Status { get; private set; }

    /// <summary>The caller-supplied idempotency key that guards against double refunds.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }
#pragma warning restore CS8618

    public Refund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
    }
}
