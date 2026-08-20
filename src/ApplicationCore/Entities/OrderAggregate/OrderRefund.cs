using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
    public string PayPalRefundId { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(string payPalRefundId, string status, decimal amount, string currency, string idempotencyKey, DateTimeOffset createdAt)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
        Amount = amount;
        Currency = currency;
        IdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;
    }
}
