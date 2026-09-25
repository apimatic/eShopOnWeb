using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against the captured payment. Multiple partial refunds may exist.
/// The <see cref="IdempotencyKey"/> is the caller-supplied key; repeating a request under the
/// same key must not create a second refund.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.Negative(amount, nameof(amount));

        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int PaymentId { get; private set; }

    /// <summary>Caller-supplied idempotency key for this refund.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal-generated refund id.</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }

    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
