using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single (full or partial) refund against a captured payment. Part of the <see cref="Payment"/> aggregate.
/// The <see cref="IdempotencyKey"/> is the caller-supplied key that makes a repeated refund request a no-op.
/// </summary>
public class Refund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }

    public Refund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        CreatedAt = DateTimeOffset.Now;
    }

    /// <summary>Caller-supplied idempotency key; a repeat under the same key must not refund twice.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal's own id for this refund.</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal's reported refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
