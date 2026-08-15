using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against a captured payment. Part of the <see cref="Payment"/> aggregate.
/// The caller-supplied <see cref="IdempotencyKey"/> makes a repeated refund request a no-op,
/// while two distinct keys represent two legitimate partial refunds.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        RefundId = refundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>PayPal's own id for this refund.</summary>
    public string RefundId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal's reported refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>Caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
