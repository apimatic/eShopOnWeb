using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : IAggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public int OrderId { get; private set; }
    public string PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.Now;

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(int orderId, string payPalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        OrderId = orderId;
        PayPalRefundId = payPalRefundId;
        Status = status;
        Amount = amount;
        Currency = currency;
        IdempotencyKey = idempotencyKey;
    }
}