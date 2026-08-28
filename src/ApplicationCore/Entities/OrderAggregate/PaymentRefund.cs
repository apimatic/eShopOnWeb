using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, string paypalRefundId, string status,
        decimal amount, DateTimeOffset createdAt)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = paypalRefundId;
        Status = status;
        Amount = amount;
        CreatedAt = createdAt;
    }

    public int OrderId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string PayPalRefundId { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateStatus(string status) => Status = status;
}
