using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    internal PaymentRefund(string idempotencyKey, string requestId, decimal amount, string currency)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRequestId = requestId;
        Amount = amount;
        Currency = currency;
        Status = "STARTED";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string PayPalRequestId { get; private set; } = string.Empty;
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public void Complete(string refundId, string status, decimal amount)
    {
        PayPalRefundId = refundId;
        Status = status;
        Amount = amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Fail()
    {
        Status = "FAILED";
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
