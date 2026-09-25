using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against a captured payment. Belongs to the <see cref="Payment"/>
/// aggregate. The <see cref="IdempotencyKey"/> is the caller-supplied key that makes repeating a
/// refund request under the same key a no-op, while two distinct partial refunds remain legitimate.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public int PaymentId { get; private set; }

    /// <summary>The caller-supplied idempotency key for this refund.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal's own id for the refund.</summary>
    public string? PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal's reported refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, decimal amount, string? payPalRefundId, string status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        PayPalRefundId = payPalRefundId;
        Status = status;
    }

    public void UpdateStatus(string status, string? payPalRefundId)
    {
        Status = status;
        if (!string.IsNullOrEmpty(payPalRefundId))
        {
            PayPalRefundId = payPalRefundId;
        }
    }
}
