using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class Refund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }

    public Refund(string idempotencyKey, string payPalRefundId, string status, decimal amount, DateTimeOffset createdAt)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Status = status;
        Amount = amount;
        CreatedAt = createdAt;
    }

    public string IdempotencyKey { get; private set; }
    public string PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
