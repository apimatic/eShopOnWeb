using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against a captured payment. A capture may be refunded several times
/// (distinct partial refunds), so refunds are modelled as a child collection of
/// <see cref="OrderPayment"/>.
/// </summary>
public class PaymentRefund : BaseEntity
{
    /// <summary>PayPal's own identifier for the refund.</summary>
    public string PayPalRefundId { get; private set; }

    /// <summary>The amount returned to the shopper for this refund.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's reported status for the refund (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>
    /// The caller-supplied idempotency key that produced this refund. Repeating a request under
    /// the same key must return this same refund rather than issuing another.
    /// </summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>When the refund was issued (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.Negative(amount, nameof(amount));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status ?? string.Empty;
        IdempotencyKey = idempotencyKey;
    }
}
