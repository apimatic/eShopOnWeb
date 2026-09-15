using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    public PaymentRefund(int orderPaymentId, string paypalRefundId, string idempotencyKeyHash,
        string status, decimal amount, DateTimeOffset createdAt)
    {
        OrderPaymentId = orderPaymentId;
        PayPalRefundId = paypalRefundId;
        IdempotencyKeyHash = idempotencyKeyHash;
        Status = status;
        Amount = amount;
        CreatedAt = createdAt;
    }

    public int OrderPaymentId { get; private set; }
    public string PayPalRefundId { get; private set; } = null!;
    public string IdempotencyKeyHash { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
