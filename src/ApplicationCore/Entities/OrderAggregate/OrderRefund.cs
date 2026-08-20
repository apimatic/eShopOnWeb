using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
#pragma warning disable CS8618
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(string paypalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        PayPalRefundId = paypalRefundId;
        Status = status;
        Amount = amount;
        Currency = currency;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public string PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateStatus(string status)
    {
        Status = status;
    }
}
