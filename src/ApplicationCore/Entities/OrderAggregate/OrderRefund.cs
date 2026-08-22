using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
#pragma warning disable CS8618
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(string paypalRefundId, string paypalRefundStatus, string idempotencyKey, decimal amount)
    {
        PayPalRefundId = paypalRefundId;
        PayPalRefundStatus = paypalRefundStatus;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string PayPalRefundId { get; private set; }
    public string PayPalRefundStatus { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateStatus(string status)
    {
        PayPalRefundStatus = status;
    }
}
