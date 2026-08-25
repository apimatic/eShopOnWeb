using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payment;

public class RefundRecord : BaseEntity
{
#pragma warning disable CS8618
    private RefundRecord() { }
#pragma warning restore CS8618

    public RefundRecord(int paymentRecordId, string payPalRefundId, decimal amount, string idempotencyKey)
    {
        PaymentRecordId = paymentRecordId;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int PaymentRecordId { get; private set; }
    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
