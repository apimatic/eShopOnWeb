using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderRefund() { }

    public OrderRefund(int orderId, string payPalRefundId, string idempotencyKey, decimal amount, string currency)
    {
        OrderId = orderId;
        PayPalRefundId = payPalRefundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        RefundedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string PayPalRefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset RefundedAt { get; private set; }
}
