using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund against a captured payment. The caller-supplied <see cref="IdempotencyKey"/>
/// makes a repeated refund request a no-op, while two distinct keys are two legitimate refunds.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, decimal amount)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
    }

    /// <summary>Caller-supplied idempotency key; also sent to PayPal as PayPal-Request-Id.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal's refund id, once known.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>PayPal's refund status (wire value, e.g. COMPLETED / PENDING).</summary>
    public string? Status { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

    public void SetProviderResult(string payPalRefundId, string? status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        PayPalRefundId = payPalRefundId;
        Status = status;
    }
}
