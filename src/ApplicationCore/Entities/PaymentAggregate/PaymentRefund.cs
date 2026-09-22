using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against an <see cref="OrderPayment"/>'s capture. Multiple partial refunds
/// can exist for one capture; each carries a distinct caller-supplied idempotency key so a repeated
/// request under the same key is recognised and not refunded twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, decimal amount)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>FK back to the owning <see cref="OrderPayment"/>.</summary>
    public int OrderPaymentId { get; private set; }

    /// <summary>Caller-supplied idempotency key. Unique per capture.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>The refunded amount (may be partial).</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal-generated refund id.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>PayPal-reported refund status (e.g. COMPLETED, PENDING).</summary>
    public string? PayPalStatus { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void SetPayPalResult(string? payPalRefundId, string? payPalStatus)
    {
        PayPalRefundId = payPalRefundId;
        PayPalStatus = payPalStatus;
    }
}
