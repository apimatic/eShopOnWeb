using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderPaymentAggregate;

/// <summary>
/// A single refund taken against the captured payment. A capture may be refunded
/// several times (distinct partial refunds), so refunds are a collection on the payment.
/// </summary>
public class Refund : BaseEntity
{
    public string PayPalRefundId { get; private set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key
    /// must not refund twice; two distinct keys are two legitimate partial refunds.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }
#pragma warning restore CS8618

    public Refund(string payPalRefundId, string idempotencyKey, decimal amount, string status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        PayPalRefundId = payPalRefundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = status;
    }
}
