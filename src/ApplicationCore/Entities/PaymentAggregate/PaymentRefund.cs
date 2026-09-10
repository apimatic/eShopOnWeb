using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against a captured payment. A capture may be refunded more than once
/// (several partial refunds), so refunds are held as a collection on the <see cref="Payment"/>.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        RefundId = refundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal's identifier for the refund.</summary>
    public string RefundId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal-reported status, e.g. COMPLETED, PENDING, FAILED, CANCELLED.</summary>
    public string Status { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
