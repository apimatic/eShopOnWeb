using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund taken against a captured <see cref="OrderPayment"/>. A payment may have many
/// refunds (partial refunds), each carrying the caller-supplied idempotency key that produced it.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string payPalRefundId, string idempotencyKey, decimal amount, string currency, string status)
    {
        PayPalRefundId = payPalRefundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal's own id for this refund (returned as the <c>refundId</c> to the caller).</summary>
    public string PayPalRefundId { get; private set; }

    /// <summary>The caller-supplied idempotency key that created this refund. Repeats are de-duplicated on it.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>PayPal refund status as reported (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
