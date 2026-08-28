using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618
    private PaymentRefund() { }
#pragma warning restore CS8618

    internal PaymentRefund(string idempotencyKey, string paypalRefundId, string status,
        decimal amount, DateTimeOffset? createdAt)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = paypalRefundId;
        Status = status;
        Amount = amount;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
