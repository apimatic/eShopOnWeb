using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618
    private PaymentRefund() { }

    public PaymentRefund(string paypalRefundId, string idempotencyKey, decimal amount, string currency)
    {
        PayPalRefundId = paypalRefundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        RefundedAt = DateTimeOffset.UtcNow;
    }

    public string PayPalRefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset RefundedAt { get; private set; }
}
