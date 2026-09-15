using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618
    private PaymentRefund() { }
#pragma warning restore CS8618

    internal PaymentRefund(string idempotencyKey, decimal amount)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = "INITIATED";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public decimal? RefundedPayPalFee { get; private set; }
    public decimal? MerchantNetDebit { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public void Complete(string refundId, string status, decimal amount,
        decimal? refundedPayPalFee, decimal? merchantNetDebit, DateTimeOffset? updatedAt)
    {
        PayPalRefundId = refundId;
        Status = status;
        Amount = amount;
        RefundedPayPalFee = refundedPayPalFee;
        MerchantNetDebit = merchantNetDebit;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }

    public void Fail()
    {
        Status = "FAILED";
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
