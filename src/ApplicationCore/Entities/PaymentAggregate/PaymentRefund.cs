using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against a captured payment. A capture can be refunded more than once
/// (distinct partial refunds), so each refund records the caller-supplied idempotency key that
/// produced it — repeating a request under the same key must not create a second refund.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
    }

    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }

    /// <summary>The caller-supplied idempotency key this refund was created under.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset RefundedAt { get; private set; } = DateTimeOffset.UtcNow;
}
