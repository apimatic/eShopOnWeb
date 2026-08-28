using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, string paypalRequestId, decimal requestedAmount)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRequestId = paypalRequestId;
        Amount = requestedAmount;
    }

    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string PayPalRequestId { get; private set; } = string.Empty;
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; } = "CREATING";
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; private set; }

    public void RecordResult(string paypalRefundId, string status, decimal amount,
        DateTimeOffset? createTime, DateTimeOffset? updateTime)
    {
        PayPalRefundId = paypalRefundId;
        Status = status;
        Amount = amount;
        CreatedAt = createTime ?? CreatedAt;
        UpdatedAt = updateTime ?? DateTimeOffset.UtcNow;
    }

    public void RecordFailure()
    {
        Status = "FAILED";
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
