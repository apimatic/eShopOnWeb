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
        Status = "RESERVED";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int PaymentRecordId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public void RecordProviderResult(string id, string status, decimal amount, DateTimeOffset? updatedAt)
    {
        PayPalRefundId = id;
        Status = status;
        Amount = amount;
        UpdatedAt = updatedAt;
    }

    public void MarkFailed()
    {
        Status = "FAILED";
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
