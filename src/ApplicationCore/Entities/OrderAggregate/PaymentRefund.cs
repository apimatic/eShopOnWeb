using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    public PaymentRefund(string payPalRefundId, string idempotencyKey, string status,
        decimal amount, string currency, DateTimeOffset createdAt)
    {
        PayPalRefundId = payPalRefundId;
        IdempotencyKey = idempotencyKey;
        Status = status;
        Amount = amount;
        Currency = currency;
        CreatedAt = createdAt;
    }

    public string PayPalRefundId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
