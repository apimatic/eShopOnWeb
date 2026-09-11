using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against a captured <see cref="OrderPayment"/>. Multiple partial refunds
/// against one capture are legitimate; each carries the caller-supplied idempotency key that
/// produced it so a repeat under the same key is not refunded twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string payPalRefundId, decimal amount, string idempotencyKey, string status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        PayPalRefundId = payPalRefundId;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal's own id for the refund resource.</summary>
    public string PayPalRefundId { get; private set; }

    /// <summary>The amount refunded by this request.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal's reported status for the refund (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
