using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund taken against a captured <see cref="Payment"/>. A capture may have many
/// (partial) refunds; each carries the caller-supplied idempotency key that produced it so a
/// repeated request under the same key is recognised rather than refunding twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(status, nameof(status));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal's own id for this refund.</summary>
    public string PayPalRefundId { get; private set; }

    /// <summary>Amount returned to the shopper for this refund.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>Caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateStatus(string status)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
    }
}
