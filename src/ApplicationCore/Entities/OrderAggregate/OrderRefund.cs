using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
    public int OrderId { get; private set; }
    public string RefundId { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public DateTimeOffset RefundedAt { get; private set; }

#pragma warning disable CS8618
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(int orderId, string refundId, decimal amount, string idempotencyKey)
    {
        OrderId = orderId;
        RefundId = refundId;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        RefundedAt = DateTimeOffset.UtcNow;
    }
}
