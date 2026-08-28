using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    public PaymentRefund(string payPalRefundId, string idempotencyKey, decimal amount,
        string currency, string status, DateTimeOffset createdAt)
    {
        PayPalRefundId = payPalRefundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        Status = status;
        CreatedAt = createdAt;
    }

    public int OrderId { get; private set; }
    public string PayPalRefundId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public void SetStatus(string status) => Status = status;
}
