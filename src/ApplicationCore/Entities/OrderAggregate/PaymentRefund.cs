using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618
    private PaymentRefund() { }

    internal PaymentRefund(string paypalRefundId, string idempotencyKey, string status,
        decimal amount, DateTimeOffset createdAt)
    {
        PayPalRefundId = paypalRefundId;
        IdempotencyKey = idempotencyKey;
        Status = status;
        Amount = amount;
        CreatedAt = createdAt;
    }

    public int OrderId { get; private set; }
    public string PayPalRefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
