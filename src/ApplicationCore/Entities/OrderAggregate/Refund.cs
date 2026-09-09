using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund taken against a captured <see cref="Payment"/>. A capture may be refunded more
/// than once (distinct partial refunds); each carries the caller-supplied idempotency key that
/// created it so a repeat under the same key can be recognised and never refunds twice.
/// </summary>
public class Refund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }

    public Refund(string paypalRefundId, string idempotencyKey, decimal amount, string status)
    {
        Guard.Against.NullOrEmpty(paypalRefundId, nameof(paypalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        PayPalRefundId = paypalRefundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal's own id for the refund resource.</summary>
    public string PayPalRefundId { get; private set; }

    /// <summary>Caller-supplied key that made this refund; repeats under the same key are deduplicated.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>Amount returned to the shopper for this refund.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's current status for the refund (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
