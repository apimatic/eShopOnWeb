using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against a captured <see cref="Payment"/>. A capture can have several
/// (distinct partial refunds), each carrying the caller-supplied idempotency key that produced it.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string refundId, decimal amount, string idempotencyKey, string status)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        RefundId = refundId;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal-generated refund id.</summary>
    public string RefundId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal's refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
