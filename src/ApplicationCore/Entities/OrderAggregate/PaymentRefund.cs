using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against a captured <see cref="Payment"/>. Refunds may be full or partial;
/// several partial refunds can exist for one capture. Each refund records the caller-supplied
/// idempotency key so a repeated request under the same key is never refunded twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, string payPalRefundId, decimal amount, string currency, string status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Currency = currency;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int PaymentId { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal's refund id (<c>/v2/payments/refunds/{id}</c>).</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>PayPal refund status, e.g. COMPLETED / PENDING.</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
