using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity, IAggregateRoot
{
    private PaymentRefund() { }

    public PaymentRefund(int orderId, string idempotencyKey, string payPalRefundId, string status,
        decimal amount, string currency)
    {
        OrderId = orderId;
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Status = status;
        Amount = amount;
        Currency = currency;
    }

    public int OrderId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string PayPalRefundId { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
