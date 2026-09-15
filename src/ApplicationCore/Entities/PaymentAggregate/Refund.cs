using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single return of captured funds to the shopper. A capture may be refunded more
/// than once (several partial refunds), so a Payment owns a collection of these.
/// The caller-supplied <see cref="IdempotencyKey"/> guarantees that repeating the same
/// refund request never returns money twice, while two distinct partial refunds each
/// carry their own key and both stand.
/// </summary>
public class Refund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }

    public Refund(string idempotencyKey, decimal amount)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int PaymentId { get; private set; }

    /// <summary>Caller-supplied key that makes the refund request idempotent.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>The refunded amount, in the payment's currency.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's own id for this refund transaction.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>PayPal's current status for the refund (e.g. COMPLETED, PENDING).</summary>
    public string? PayPalStatus { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records what PayPal reported once the refund call has returned.</summary>
    public void Confirm(string payPalRefundId, string payPalStatus)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        PayPalRefundId = payPalRefundId;
        PayPalStatus = payPalStatus;
    }
}
