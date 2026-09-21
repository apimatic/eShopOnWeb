using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against a captured payment. Belongs to an <see cref="OrderPayment"/>.
/// The <see cref="IdempotencyKey"/> is the caller-supplied key that makes a repeated refund request
/// a no-op rather than a second refund.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, decimal amount, string? payPalRefundId, string status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        PayPalRefundId = payPalRefundId;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Caller-supplied idempotency key for this refund request.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>Amount refunded, in the order's currency.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's own id for the refund (the state PayPal owns for it).</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>PayPal's current status for the refund (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
