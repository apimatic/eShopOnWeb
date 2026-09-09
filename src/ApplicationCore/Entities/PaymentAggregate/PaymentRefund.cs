using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against a captured <see cref="Payment"/>. Carries PayPal's own
/// refund id and status so a later request can reconcile it, plus the caller-supplied
/// idempotency key that guards against a repeated refund request refunding twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Caller-supplied key; a repeat under the same key returns this refund unchanged.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal's refund id (state PayPal owns).</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal's current status for this refund, e.g. COMPLETED / PENDING.</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
