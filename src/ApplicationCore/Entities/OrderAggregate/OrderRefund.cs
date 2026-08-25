using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
    #pragma warning disable CS8618
    private OrderRefund() {}

    public OrderRefund(string refundId, string idempotencyKey, decimal amount)
    {
        RefundId = refundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        RefundedAt = DateTimeOffset.UtcNow;
    }

    public string RefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset RefundedAt { get; private set; }
}
