using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class OrderRefund : BaseEntity
{
    #pragma warning disable CS8618
    private OrderRefund() { }

    public OrderRefund(int orderPaymentId, string idempotencyKey, string? payPalRefundId, decimal amount)
    {
        OrderPaymentId = orderPaymentId;
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        RefundedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset RefundedAt { get; private set; }
}
