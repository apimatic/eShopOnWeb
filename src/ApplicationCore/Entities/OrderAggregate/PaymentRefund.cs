using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against a captured <see cref="Payment"/>. Owned by <see cref="Payment"/>.
/// The <see cref="IdempotencyKey"/> is the caller-supplied key that makes a repeated refund request
/// a no-op while still allowing distinct partial refunds.
/// </summary>
public class PaymentRefund
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        RefundId = refundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The PayPal-generated ID for the refund.</summary>
    public string RefundId { get; private set; }

    /// <summary>The refunded amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The PayPal refund status (for example, COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
