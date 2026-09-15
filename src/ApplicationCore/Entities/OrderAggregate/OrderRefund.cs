using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
    private OrderRefund() { }
    public OrderRefund(string idempotencyKey, string payPalRefundId, string payPalStatus,
        decimal amount, DateTimeOffset createdAt)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        PayPalStatus = payPalStatus;
        Amount = amount;
        CreatedAt = createdAt;
    }
    public int OrderId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string PayPalRefundId { get; private set; } = string.Empty;
    public string PayPalStatus { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
