using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    internal PaymentRefund(string idempotencyKey, string paypalRefundId, string status, decimal amount,
        string currency)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = paypalRefundId;
        Status = status;
        Amount = amount;
        Currency = currency;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string PayPalRefundId { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
