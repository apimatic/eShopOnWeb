using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    internal PaymentRefund(int orderPaymentId, string idempotencyKey, string payPalRefundId,
        string status, decimal amount, DateTimeOffset? createdAt)
    {
        OrderPaymentId = orderPaymentId;
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Status = status;
        Amount = amount;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string PayPalRefundId { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
