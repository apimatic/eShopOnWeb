using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund against a captured <see cref="Payment"/>. Refunds are identified by a
/// caller-supplied idempotency key so a retried request never refunds twice, while two
/// distinct partial refunds (distinct keys) of the same capture remain legitimate.
/// </summary>
public class Refund : BaseEntity
{
    public const string StatusPending = "PENDING";
    public const string StatusCompleted = "COMPLETED";
    public const string StatusFailed = "FAILED";

#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }
#pragma warning restore CS8618

    public Refund(string idempotencyKey, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        Status = StatusPending;
    }

    /// <summary>Caller-supplied idempotency key for this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    /// <summary>PayPal's refund id, once the refund has been accepted.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>PayPal's current status for the refund (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>True while the refund is in-flight or settled (i.e. it consumes refundable balance).</summary>
    public bool IsActive => Status != StatusFailed;

    public void MarkAccepted(string payPalRefundId, string status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        PayPalRefundId = payPalRefundId;
        Status = string.IsNullOrEmpty(status) ? StatusCompleted : status;
    }

    public void MarkFailed() => Status = StatusFailed;
}
