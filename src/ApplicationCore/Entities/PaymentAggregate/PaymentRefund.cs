using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618
    private PaymentRefund() { }

    public PaymentRefund(int paymentId, string payPalRefundId, decimal amount, string idempotencyKey)
    {
        PaymentId = paymentId;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        RefundedAt = DateTimeOffset.UtcNow;
    }

    public int PaymentId { get; private set; }
    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset RefundedAt { get; private set; }
}
