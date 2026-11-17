using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

#pragma warning disable CS8618 // Required by Entity Framework

public class OrderRefund : BaseEntity, IAggregateRoot
{
    private OrderRefund() { }

    public OrderRefund(int orderId, string finixRefundTransferId, decimal amount, string idempotencyKey, string state)
    {
        OrderId = orderId;
        FinixRefundTransferId = finixRefundTransferId;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        State = state;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string FinixRefundTransferId { get; private set; }
    public decimal Amount { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
