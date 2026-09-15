using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against the captured payment of an order. Owned by <see cref="OrderPayment"/>.
/// Each refund records PayPal's own refund id and status plus the caller-supplied idempotency key that
/// produced it, so a repeated request under the same key can be recognised and never refunds twice.
/// </summary>
public class PaymentRefund
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        RefundId = refundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal's generated id for this refund.</summary>
    public string RefundId { get; private set; }

    /// <summary>The amount refunded, in the payment currency.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's status for the refund (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
