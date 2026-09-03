using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against a <see cref="Payment"/>'s capture. Carries the caller-supplied
/// idempotency key so a repeated request under the same key is not refunded twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, decimal amount, string? payPalRefundId, string? status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        PayPalRefundId = payPalRefundId;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>The refunded amount (positive).</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal-generated refund id (the <c>refundId</c> returned to the caller).</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>PayPal's current status for this refund (e.g. COMPLETED, PENDING).</summary>
    public string? Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
