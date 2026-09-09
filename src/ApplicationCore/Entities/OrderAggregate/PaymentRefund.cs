using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against a captured <see cref="Payment"/>. Carries the caller-supplied
/// idempotency key so that repeating a refund request under the same key returns the original
/// refund instead of issuing a second one, while two distinct keys remain two legitimate refunds.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal's own id for the refund (from <c>/v2/payments/captures/{id}/refund</c>).</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>The refund status as reported by PayPal (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>The idempotency key supplied by the caller for this refund request.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
