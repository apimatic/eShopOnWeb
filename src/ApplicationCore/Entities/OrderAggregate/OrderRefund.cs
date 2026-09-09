using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against the captured payment for an order. Owned by
/// <see cref="OrderPayment"/>. Carries the PayPal refund id and status so that the
/// state PayPal owns is recorded locally, plus the caller-supplied idempotency key so
/// a repeated refund request under the same key is never applied twice.
/// </summary>
public class OrderRefund // ValueObject owned by OrderPayment
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(string payPalRefundId, decimal amount, string currency, string status, string idempotencyKey)
    {
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Currency = currency;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The PayPal-generated id for this refund.</summary>
    public string PayPalRefundId { get; private set; }

    /// <summary>The amount refunded in <see cref="Currency"/>.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>PayPal's reported refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A refund that failed or was cancelled does not consume refundable balance.</summary>
    public bool CountsTowardRefundedTotal =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}
