using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund taken against the order's captured payment. Refunds are keyed by a
/// caller-supplied idempotency key so that repeating a refund request under the same key does
/// not refund twice, while two distinct partial refunds remain separate rows.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, decimal amount)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Caller-supplied key that makes a refund request idempotent.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal's refund id (owned by PayPal, needed to look the refund up later).</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>Amount refunded for this refund, in the order currency.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's current status for the refund (e.g. COMPLETED, PENDING).</summary>
    public string? Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void SetResult(string payPalRefundId, string status)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
    }
}
