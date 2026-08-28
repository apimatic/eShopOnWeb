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
    }

    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; } = "CREATING";
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; private set; }

    public void RecordProviderResult(string id, string? status, decimal amount, DateTimeOffset? updatedAt)
    {
        PayPalRefundId = id;
        Status = status ?? "UNKNOWN";
        Amount = amount;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }
}
