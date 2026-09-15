using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, decimal amount, string currency)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        Status = "INITIATED";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? StatusReason { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public void RecordProviderResult(string refundId, string status, decimal amount,
        DateTimeOffset? updatedAt, string? reason)
    {
        PayPalRefundId = refundId;
        Status = status;
        Amount = amount;
        UpdatedAt = updatedAt;
        StatusReason = reason;
    }

    public void RecordFailure(string reason)
    {
        Status = "FAILED";
        StatusReason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
