using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(int orderId, string finixRefundId, long amountCents, string state, string idempotencyKey)
    {
        OrderId = orderId;
        FinixRefundId = finixRefundId;
        AmountCents = amountCents;
        State = state;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string FinixRefundId { get; private set; }
    public long AmountCents { get; private set; }
    public string State { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
