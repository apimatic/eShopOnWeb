using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    public PaymentRefund(string id, string idempotencyKey, string status, decimal amount, DateTimeOffset createdAt)
    {
        PayPalRefundId = id;
        IdempotencyKey = idempotencyKey;
        Status = status;
        Amount = amount;
        CreatedAt = createdAt;
    }

    public int PaymentId { get; private set; }
    public string PayPalRefundId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
