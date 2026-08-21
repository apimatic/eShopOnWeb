using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against the captured payment of an <see cref="Order"/>.
/// Part of the Order aggregate — created only through <see cref="OrderPayment.RecordRefund"/>.
/// </summary>
public class OrderRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(string refundId, decimal amount, string status, string idempotencyKey, DateTimeOffset createdAt)
    {
        RefundId = refundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;
    }

    /// <summary>PayPal's own id for this refund.</summary>
    public string RefundId { get; private set; }

    /// <summary>Amount refunded, in the order currency.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>Caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
