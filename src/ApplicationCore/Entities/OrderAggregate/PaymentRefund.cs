using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, string paypalRefundId, decimal amount,
        string currency, string status, DateTimeOffset createdAt)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = paypalRefundId;
        Amount = amount;
        Currency = currency;
        Status = status;
        CreatedAt = createdAt;
    }

    public string IdempotencyKey { get; private set; }
    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Refresh(string status) => Status = status;
}
