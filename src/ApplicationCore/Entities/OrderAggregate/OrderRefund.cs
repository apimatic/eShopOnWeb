using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
#pragma warning disable CS8618
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(int orderId, string payPalRefundId, decimal amount, string idempotencyKey)
    {
        OrderId = orderId;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        RefundedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset RefundedAt { get; private set; }
}
