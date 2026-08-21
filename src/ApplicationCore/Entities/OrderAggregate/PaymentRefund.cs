using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund PayPal made against a capture. Carries PayPal's refund id and the
/// caller-supplied idempotency key that produced it, so a repeated request under the same key
/// can be recognised and never refunds twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string refundId, decimal amount, string? status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        RefundId = refundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>PayPal's refund id.</summary>
    public string RefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string? Status { get; private set; }

    /// <summary>Caller-supplied idempotency key used for this refund (also sent to PayPal).</summary>
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
