using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single (full or partial) refund of the order's capture. Part of the <see cref="Order"/>
/// aggregate, owned by its <see cref="Payment"/>. Carries the caller-supplied idempotency key
/// so a replayed refund request is recognised and never refunds twice.
/// </summary>
public class Refund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }
#pragma warning restore CS8618

    public Refund(string idempotencyKey, decimal amount)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = RefundStatus.Pending;
        CreatedAt = DateTimeOffset.Now;
    }

    /// <summary>The caller-supplied key that makes this refund request idempotent.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>The amount refunded, in the order's currency.</summary>
    public decimal Amount { get; private set; }

    public RefundStatus Status { get; private set; }

    /// <summary>PayPal's own id for this refund.</summary>
    public string? PayPalRefundId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>True when PayPal reported the refund did not go through.</summary>
    public bool IsUnsuccessful => Status == RefundStatus.Failed || Status == RefundStatus.Cancelled;

    /// <summary>Records PayPal's reported outcome for this refund.</summary>
    public void SetResult(string payPalRefundId, RefundStatus status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        PayPalRefundId = payPalRefundId;
        Status = status;
    }
}
