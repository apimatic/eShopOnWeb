using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against an order's captured <see cref="Payment"/>. A capture can
/// have several distinct partial refunds; each carries the caller-supplied idempotency key
/// that produced it so a repeated request is never refunded twice.
/// </summary>
public class PaymentRefund
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        RefundedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The caller-supplied idempotency key that created this refund.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal's own id for the refund transaction.</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal's reported status for the refund (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset RefundedAt { get; private set; }
}
