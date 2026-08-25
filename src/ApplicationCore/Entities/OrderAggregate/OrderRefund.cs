using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
    #pragma warning disable CS8618
    private OrderRefund() { }

    public OrderRefund(int orderId, string refundId, string idempotencyKey, decimal amount)
    {
        OrderId = orderId;
        RefundId = refundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string RefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
